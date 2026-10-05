#!/usr/bin/env python3
"""Generate and validate material-bound cardinal speech-shaped-noise profiles.

The tool is intentionally offline. It reads the frozen measurement WAV files,
derives one equal-item long-term spectrum per voice, designs the two FIR sets
required by protocol v1, and writes deterministic JSON profiles plus a report.
It never opens an audio endpoint and does not enable application playback.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import struct
import sys
import wave

import numpy as np


PROTOCOL_ID = "cardinal-speech-shaped-noise"
PROTOCOL_VERSION = 1
MATERIAL_IDS = (
    "de-DE-personal-cardinal-numbers-christoph-v1",
    "de-DE-personal-cardinal-numbers-katja-v1",
)
MATERIAL_DIRECTORIES = {
    "de-DE-personal-cardinal-numbers-christoph-v1": "personal-cardinal-numbers-christoph-v1",
    "de-DE-personal-cardinal-numbers-katja-v1": "personal-cardinal-numbers-katja-v1",
}
ANALYSIS_RATE = 22_050
FRAME_MS = 25
HOP_MS = 10
FFT_SIZE = 2_048
FRAME_SAMPLES = 551  # round-half-up(22050 * 0.025)
HOP_SAMPLES = 221  # round-half-up(22050 * 0.010)
ACTIVE_ABSOLUTE_DBFS = -70.0
ACTIVE_RELATIVE_DB = -40.0
FIR_TAPS = 4_097
OUTPUT_RATES = (44_100, 48_000)
LOWER_HZ = 125.0
UPPER_HZ = 8_000.0
VALIDATION_SECONDS = 60
SPECTRUM_TOLERANCE_DB = 1.0
SNR_TOLERANCE_DB = 0.1
SNR_BOUNDARIES_DB = (-20.0, 30.0)
DIGITAL_ATTENUATION_BOUNDARIES_DB = (-96.0, 0.0)
PRE_ROLL_SECONDS = 0.2
POST_ROLL_SECONDS = 0.2
FADE_SECONDS = 0.02
PROFILE_SCHEMA_VERSION = 1
TOOL_VERSION = "1.0.0"
VALIDATION_SEED = 0x6A09E667


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def canonical_json_bytes(value: object) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n").encode("utf-8")


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(canonical_json_bytes(value))


def db_to_amplitude(value_db: float) -> float:
    return 10.0 ** (value_db / 20.0)


def amplitude_to_dbfs(value: float) -> float:
    return 20.0 * math.log10(max(value, np.finfo(np.float64).tiny))


def read_pcm16_mono(path: Path) -> tuple[int, np.ndarray]:
    with wave.open(os.fspath(path), "rb") as wav:
        if wav.getnchannels() != 1 or wav.getsampwidth() != 2:
            raise ValueError(f"{path}: erwartet PCM16 mono")
        sample_rate = wav.getframerate()
        if wav.getcomptype() != "NONE":
            raise ValueError(f"{path}: komprimierte WAV ist nicht zulässig")
        samples = np.frombuffer(wav.readframes(wav.getnframes()), dtype="<i2").astype(np.float64)
    return sample_rate, samples / 32768.0


def frames(samples: np.ndarray) -> np.ndarray:
    if samples.size < FRAME_SAMPLES:
        samples = np.pad(samples, (0, FRAME_SAMPLES - samples.size))
    count = 1 + int(math.ceil((samples.size - FRAME_SAMPLES) / HOP_SAMPLES))
    required = (count - 1) * HOP_SAMPLES + FRAME_SAMPLES
    if required > samples.size:
        samples = np.pad(samples, (0, required - samples.size))
    view = np.lib.stride_tricks.sliding_window_view(samples, FRAME_SAMPLES)
    return view[::HOP_SAMPLES][:count]


def analyze_item(samples: np.ndarray, hann: np.ndarray) -> tuple[np.ndarray, dict[str, float | int]]:
    item_frames = frames(samples)
    frame_rms = np.sqrt(np.mean(item_frames * item_frames, axis=1))
    threshold = max(
        db_to_amplitude(ACTIVE_ABSOLUTE_DBFS),
        float(np.max(frame_rms)) * db_to_amplitude(ACTIVE_RELATIVE_DB),
    )
    active = np.flatnonzero(frame_rms >= threshold)
    if active.size == 0:
        raise ValueError("kein aktives Sprachfenster")

    region_start = int(active[0] * HOP_SAMPLES)
    region_end = min(int(active[-1] * HOP_SAMPLES + FRAME_SAMPLES), samples.size)
    region = samples[region_start:region_end]
    speech_rms = float(np.sqrt(np.mean(region * region)))
    if not math.isfinite(speech_rms) or speech_rms <= 0.0:
        raise ValueError("ungültiger Sprach-RMS")

    normalized = item_frames[active] / speech_rms
    transformed = np.fft.rfft(normalized * hann, n=FFT_SIZE, axis=1)
    psd = np.mean(np.abs(transformed) ** 2, axis=0) / float(np.sum(hann * hann))
    return psd, {
        "activeFrameCount": int(active.size),
        "firstActiveSample": region_start,
        "lastActiveSampleExclusive": region_end,
        "speechRmsDbfs": amplitude_to_dbfs(speech_rms),
        "durationSeconds": samples.size / ANALYSIS_RATE,
        "activeRegionSeconds": (region_end - region_start) / ANALYSIS_RATE,
    }


def third_octave_centers() -> np.ndarray:
    return np.asarray([LOWER_HZ * (2.0 ** (index / 3.0)) for index in range(19)], dtype=np.float64)


def band_mean_psd(psd: np.ndarray, frequencies: np.ndarray, centers: np.ndarray) -> np.ndarray:
    values: list[float] = []
    for center in centers:
        lower = center / (2.0 ** (1.0 / 6.0))
        upper = center * (2.0 ** (1.0 / 6.0))
        mask = (frequencies >= lower) & (frequencies < upper)
        if not np.any(mask):
            raise ValueError(f"kein FFT-Bin im Terzband {center:.3f} Hz")
        values.append(float(np.mean(psd[mask])))
    result = np.asarray(values, dtype=np.float64)
    if np.any(~np.isfinite(result)) or np.any(result <= 0.0):
        raise ValueError("ungültiges Terzbandspektrum")
    return result


def desired_magnitude(frequencies: np.ndarray, centers: np.ndarray, band_psd: np.ndarray) -> np.ndarray:
    result = np.zeros_like(frequencies, dtype=np.float64)
    lower_edge = centers[0] / (2.0 ** (1.0 / 6.0))
    upper_edge = centers[-1] * (2.0 ** (1.0 / 6.0))
    mask = (frequencies >= lower_edge) & (frequencies <= upper_edge)
    log_centers = np.log2(centers)
    log_power = np.log(np.maximum(band_psd, np.finfo(np.float64).tiny))
    interpolated_power = np.exp(np.interp(np.log2(frequencies[mask]), log_centers, log_power))
    result[mask] = np.sqrt(interpolated_power)
    maximum = float(np.max(result))
    if maximum <= 0.0:
        raise ValueError("leere Filterzielkurve")
    return result / maximum


def design_fir_once(sample_rate: int, centers: np.ndarray, control_psd: np.ndarray) -> np.ndarray:
    design_size = 65_536
    frequencies = np.fft.rfftfreq(design_size, d=1.0 / sample_rate)
    magnitude = desired_magnitude(frequencies, centers, control_psd)
    zero_phase = np.fft.fftshift(np.fft.irfft(magnitude, n=design_size))
    half = FIR_TAPS // 2
    middle = design_size // 2
    impulse = zero_phase[middle - half:middle + half + 1].copy()
    impulse *= np.blackman(FIR_TAPS)
    energy = float(np.sqrt(np.sum(impulse * impulse)))
    if not math.isfinite(energy) or energy <= 0.0:
        raise ValueError("ungültiger FIR-Filter")
    impulse /= energy
    if not np.allclose(impulse, impulse[::-1], atol=1e-14, rtol=0.0):
        raise ValueError("FIR-Filter ist nicht symmetrisch")
    return impulse


def design_fir_for_band_target(sample_rate: int, centers: np.ndarray, band_psd: np.ndarray) -> np.ndarray:
    # Frequency interpolation and finite-window truncation slightly change the
    # energy averaged across a third-octave band. Calibrate the control points
    # against the response of the actual 4097-tap filter instead of weakening
    # the validation tolerance.
    control_psd = band_psd.copy()
    impulse = design_fir_once(sample_rate, centers, control_psd)
    evaluation_size = 131_072
    frequencies = np.fft.rfftfreq(evaluation_size, d=1.0 / sample_rate)
    for _ in range(24):
        response_psd = np.abs(np.fft.rfft(impulse, n=evaluation_size)) ** 2
        achieved = band_mean_psd(response_psd, frequencies, centers)
        correction = band_psd / achieved
        control_psd *= np.sqrt(correction)
        control_psd /= float(np.max(control_psd))
        impulse = design_fir_once(sample_rate, centers, control_psd)
    return impulse


def design_fir(sample_rate: int, centers: np.ndarray, band_psd: np.ndarray) -> np.ndarray:
    # A 25-ms Welch window sees the long FIR transition at the lowest band
    # slightly differently than a pointwise frequency response. Calibrate with
    # a separate deterministic sequence and reserve VALIDATION_SEED for the
    # independent 60-second acceptance measurement.
    adjusted_target = band_psd.copy()
    calibration_noise = xorshift32_noise(sample_rate * 20, 0xBB67AE85 ^ sample_rate)
    impulse = design_fir_for_band_target(sample_rate, centers, adjusted_target)
    for _ in range(4):
        filtered = fft_convolve_same(calibration_noise, impulse)
        frequencies, measured_psd = estimate_noise_psd(filtered, sample_rate)
        measured_bands = band_mean_psd(measured_psd, frequencies, centers)
        adjusted_target *= np.power(band_psd / measured_bands, 0.75)
        adjusted_target /= float(np.max(adjusted_target))
        impulse = design_fir_for_band_target(sample_rate, centers, adjusted_target)
    return impulse


def xorshift32_noise(sample_count: int, seed: int) -> np.ndarray:
    state = int(seed) & 0xFFFFFFFF
    if state == 0:
        state = 0x9E3779B9
    output = np.empty(sample_count, dtype=np.float64)
    scale = 2.0 / 4_294_967_296.0
    for index in range(sample_count):
        state ^= (state << 13) & 0xFFFFFFFF
        state ^= state >> 17
        state ^= (state << 5) & 0xFFFFFFFF
        state &= 0xFFFFFFFF
        output[index] = state * scale - 1.0
    return output


def fft_convolve_same(samples: np.ndarray, impulse: np.ndarray) -> np.ndarray:
    block_size = 65_536
    fft_size = 1 << int(math.ceil(math.log2(block_size + impulse.size - 1)))
    response = np.fft.rfft(impulse, n=fft_size)
    full = np.zeros(samples.size + impulse.size - 1, dtype=np.float64)
    for start in range(0, samples.size, block_size):
        block = samples[start:start + block_size]
        filtered = np.fft.irfft(np.fft.rfft(block, n=fft_size) * response, n=fft_size)
        used = min(filtered.size, full.size - start)
        full[start:start + used] += filtered[:used]
    delay = (impulse.size - 1) // 2
    return full[delay:delay + samples.size]


def estimate_noise_psd(samples: np.ndarray, sample_rate: int) -> tuple[np.ndarray, np.ndarray]:
    frame_length = max(8, int(math.floor(sample_rate * FRAME_MS / 1000.0 + 0.5)))
    hop = max(1, int(math.floor(sample_rate * HOP_MS / 1000.0 + 0.5)))
    local_fft_size = 1
    while local_fft_size < frame_length:
        local_fft_size <<= 1
    local_fft_size = max(local_fft_size, 2_048 if sample_rate == ANALYSIS_RATE else 4_096)
    count = 1 + (samples.size - frame_length) // hop
    selected = np.lib.stride_tricks.sliding_window_view(samples, frame_length)[::hop][:count]
    window = np.hanning(frame_length)
    accumulated = np.zeros(local_fft_size // 2 + 1, dtype=np.float64)
    batch_size = 512
    for start in range(0, selected.shape[0], batch_size):
        batch = selected[start:start + batch_size] * window
        transformed = np.fft.rfft(batch, n=local_fft_size, axis=1)
        accumulated += np.sum(np.abs(transformed) ** 2, axis=0)
    accumulated /= selected.shape[0] * float(np.sum(window * window))
    return np.fft.rfftfreq(local_fft_size, d=1.0 / sample_rate), accumulated


def validate_spectrum(impulse: np.ndarray, sample_rate: int, centers: np.ndarray, target_psd: np.ndarray) -> dict[str, object]:
    sample_count = sample_rate * VALIDATION_SECONDS
    first = fft_convolve_same(xorshift32_noise(sample_count, VALIDATION_SEED), impulse)
    repeat = fft_convolve_same(xorshift32_noise(sample_count, VALIDATION_SEED), impulse)
    alternate = fft_convolve_same(xorshift32_noise(sample_count, VALIDATION_SEED + 1), impulse)
    if not np.array_equal(first, repeat):
        raise ValueError("identischer Seed erzeugt keine bitgleiche Float64-Ausgabe")
    if np.array_equal(first, alternate):
        raise ValueError("unterschiedliche Seeds erzeugen dieselbe Ausgabe")

    frequencies, measured_psd = estimate_noise_psd(first, sample_rate)
    measured_bands = band_mean_psd(measured_psd, frequencies, centers)
    target_db = 10.0 * np.log10(target_psd)
    measured_db = 10.0 * np.log10(measured_bands)
    raw_deviations = measured_db - target_db
    # Overall noise gain is deliberately free because each presentation is
    # RMS-scaled to its requested SNR. Remove the gain that minimizes the
    # worst absolute spectral-shape error.
    offset = float((np.max(raw_deviations) + np.min(raw_deviations)) / 2.0)
    deviations = raw_deviations - offset
    maximum_deviation = float(np.max(np.abs(deviations)))
    if maximum_deviation > SPECTRUM_TOLERANCE_DB:
        worst_index = int(np.argmax(np.abs(deviations)))
        raise ValueError(
            f"{sample_rate} Hz: Terzbandabweichung {maximum_deviation:.3f} dB bei "
            f"{centers[worst_index]:.1f} Hz überschreitet {SPECTRUM_TOLERANCE_DB:.1f} dB; "
            f"Abweichungen {[round(float(value), 3) for value in deviations]}"
        )
    return {
        "sampleRate": sample_rate,
        "durationSeconds": VALIDATION_SECONDS,
        "seed": VALIDATION_SEED,
        "float64Sha256": hashlib.sha256(first.astype("<f8", copy=False).tobytes()).hexdigest(),
        "alternateSeedFloat64Sha256": hashlib.sha256(alternate.astype("<f8", copy=False).tobytes()).hexdigest(),
        "maximumThirdOctaveDeviationDb": maximum_deviation,
        "thirdOctaveDeviationDb": [float(value) for value in deviations],
    }


def resample_linear(samples: np.ndarray, source_rate: int, target_rate: int) -> np.ndarray:
    target_count = int(math.floor(samples.size * target_rate / source_rate + 0.5))
    source_positions = np.arange(target_count, dtype=np.float64) * source_rate / target_rate
    source_positions = np.minimum(source_positions, samples.size - 1)
    return np.interp(source_positions, np.arange(samples.size, dtype=np.float64), samples)


def presentation_noise(base_noise: np.ndarray, speech_count: int, sample_rate: int) -> np.ndarray:
    pre = int(math.floor(PRE_ROLL_SECONDS * sample_rate + 0.5))
    post = int(math.floor(POST_ROLL_SECONDS * sample_rate + 0.5))
    total = pre + speech_count + post
    if total > base_noise.size:
        raise ValueError("Validierungsrauschen ist zu kurz")
    result = base_noise[:total].copy()
    fade = int(math.floor(FADE_SECONDS * sample_rate + 0.5))
    phase = np.arange(fade, dtype=np.float64) / max(1, fade - 1)
    envelope = 0.5 - 0.5 * np.cos(np.pi * phase)
    result[:fade] *= envelope
    result[-fade:] *= envelope[::-1]
    return result


def validate_all_mix_boundaries(
    items: list[dict[str, object]],
    impulse: np.ndarray,
    sample_rate: int,
) -> dict[str, object]:
    maximum_duration = max(float(item["durationSeconds"]) for item in items)
    base_count = int(math.ceil((maximum_duration + PRE_ROLL_SECONDS + POST_ROLL_SECONDS + 1.0) * sample_rate))
    base_noise = fft_convolve_same(xorshift32_noise(base_count, VALIDATION_SEED ^ sample_rate), impulse)
    rendered = 0
    blocked = 0
    max_snr_error = 0.0
    max_finite_peak = 0.0
    for item in items:
        source = item["samples"]
        assert isinstance(source, np.ndarray)
        speech = resample_linear(source, ANALYSIS_RATE, sample_rate)
        start = int(math.floor(int(item["firstActiveSample"]) * sample_rate / ANALYSIS_RATE + 0.5))
        end = int(math.floor(int(item["lastActiveSampleExclusive"]) * sample_rate / ANALYSIS_RATE + 0.5))
        end = min(end, speech.size)
        if end <= start:
            raise ValueError(f"{item['stimulusId']}: ungültiges resampeltes RMS-Fenster")
        speech_region = speech[start:end]
        noise = presentation_noise(base_noise, speech.size, sample_rate)
        pre = int(math.floor(PRE_ROLL_SECONDS * sample_rate + 0.5))
        noise_region = noise[pre + start:pre + end]
        speech_rms = float(np.sqrt(np.mean(speech_region * speech_region)))
        raw_noise_rms = float(np.sqrt(np.mean(noise_region * noise_region)))
        if speech_rms <= 0.0 or raw_noise_rms <= 0.0:
            raise ValueError(f"{item['stimulusId']}: ungültige RMS-Basis")

        for snr_db in SNR_BOUNDARIES_DB:
            target_noise_rms = speech_rms / db_to_amplitude(snr_db)
            scaled_noise = noise * (target_noise_rms / raw_noise_rms)
            measured_noise_rms = float(np.sqrt(np.mean(scaled_noise[pre + start:pre + end] ** 2)))
            measured_snr = 20.0 * math.log10(speech_rms / measured_noise_rms)
            max_snr_error = max(max_snr_error, abs(measured_snr - snr_db))
            for attenuation_db in DIGITAL_ATTENUATION_BOUNDARIES_DB:
                gain = db_to_amplitude(attenuation_db)
                mixed = scaled_noise * gain
                mixed[pre:pre + speech.size] += speech * gain
                if not np.all(np.isfinite(mixed)):
                    raise ValueError(f"{item['stimulusId']}: nicht endliche Mixsamples")
                peak = float(np.max(np.abs(mixed)))
                max_finite_peak = max(max_finite_peak, peak)
                rendered += 1
                if peak > 1.0:
                    blocked += 1
    if max_snr_error > SNR_TOLERANCE_DB:
        raise ValueError(
            f"{sample_rate} Hz: SNR-Abweichung {max_snr_error:.6f} dB überschreitet {SNR_TOLERANCE_DB:.1f} dB"
        )
    return {
        "sampleRate": sample_rate,
        "stimulusCount": len(items),
        "snrBoundariesDb": list(SNR_BOUNDARIES_DB),
        "digitalAttenuationBoundariesDb": list(DIGITAL_ATTENUATION_BOUNDARIES_DB),
        "renderCaseCount": rendered,
        "acceptedCaseCount": rendered - blocked,
        "blockedForFullScaleCaseCount": blocked,
        "maximumSnrErrorDb": max_snr_error,
        "maximumUnblockedOrBlockedFloatPeak": max_finite_peak,
        "preRollSeconds": PRE_ROLL_SECONDS,
        "postRollSeconds": POST_ROLL_SECONDS,
        "edgeFadeSeconds": FADE_SECONDS,
        "channelValidation": "mono profile; left/right isolation remains a renderer responsibility",
    }


def load_and_analyze_pack(repository: Path, material_id: str) -> tuple[dict[str, object], list[dict[str, object]]]:
    package = repository / "stimuli" / "de-DE" / MATERIAL_DIRECTORIES[material_id]
    catalog_path = package / "catalog.json"
    index_path = package / "audio-index.json"
    catalog_hash = sha256_file(catalog_path)
    index_hash = sha256_file(index_path)
    catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
    index = json.loads(index_path.read_text(encoding="utf-8"))
    if catalog.get("id") != material_id or index.get("catalogId") != material_id:
        raise ValueError(f"{material_id}: Katalog-/Audioindex-ID stimmt nicht")
    if index.get("catalogSha256") != catalog_hash:
        raise ValueError(f"{material_id}: Kataloghash im Audioindex stimmt nicht")
    audio_format = index.get("audioFormat", {})
    if audio_format != {"encoding": "PCM_SIGNED", "sampleRate": ANALYSIS_RATE, "bitsPerSample": 16, "channels": 1}:
        raise ValueError(f"{material_id}: unerwartetes Messaudioformat {audio_format}")
    entries = index.get("entries", [])
    if len(entries) != 900:
        raise ValueError(f"{material_id}: erwartet 900 Audioeinträge, gefunden {len(entries)}")
    catalog_lists = catalog.get("lists", [])
    catalog_items = [item for stimulus_list in catalog_lists for item in stimulus_list.get("items", [])]
    if len(catalog_lists) != 36 or any(len(stimulus_list.get("items", [])) != 25 for stimulus_list in catalog_lists):
        raise ValueError(f"{material_id}: erwartet 36 Kataloglisten mit je 25 Einträgen")
    catalog_ids = {str(item.get("id")) for item in catalog_items}
    catalog_responses = sorted(int(item.get("canonicalResponse")) for item in catalog_items)
    if len(catalog_ids) != 900 or catalog_responses != list(range(100, 1_000)):
        raise ValueError(f"{material_id}: Katalog enthält nicht genau die Werte 100 bis 999")

    hann = np.hanning(FRAME_SAMPLES)
    aggregate = np.zeros(FFT_SIZE // 2 + 1, dtype=np.float64)
    analyzed: list[dict[str, object]] = []
    seen: set[str] = set()
    for entry in entries:
        stimulus_id = str(entry["stimulusId"])
        if stimulus_id in seen:
            raise ValueError(f"{material_id}: doppelter Stimulus {stimulus_id}")
        seen.add(stimulus_id)
        audio_path = package / Path(str(entry["audioFile"]).replace("/", os.sep))
        if audio_path.stat().st_size != int(entry["byteLength"]):
            raise ValueError(f"{material_id}/{stimulus_id}: Dateigröße stimmt nicht")
        if sha256_file(audio_path) != entry["sha256"]:
            raise ValueError(f"{material_id}/{stimulus_id}: WAV-Hash stimmt nicht")
        sample_rate, samples = read_pcm16_mono(audio_path)
        if sample_rate != ANALYSIS_RATE:
            raise ValueError(f"{material_id}/{stimulus_id}: erwartet {ANALYSIS_RATE} Hz")
        try:
            psd, metrics = analyze_item(samples, hann)
        except ValueError as error:
            raise ValueError(f"{material_id}/{stimulus_id}: {error}") from error
        aggregate += psd
        analyzed.append({"stimulusId": stimulus_id, "samples": samples, **metrics})

    if seen != catalog_ids:
        raise ValueError(f"{material_id}: Katalog und Audioindex referenzieren unterschiedliche Stimuli")

    aggregate /= len(analyzed)
    frequencies = np.fft.rfftfreq(FFT_SIZE, d=1.0 / ANALYSIS_RATE)
    centers = third_octave_centers()
    bands = band_mean_psd(aggregate, frequencies, centers)
    relative_db = 10.0 * np.log10(bands / float(np.max(bands)))
    metadata: dict[str, object] = {
        "materialId": material_id,
        "packagePath": package.relative_to(repository).as_posix(),
        "catalogSha256": catalog_hash,
        "audioIndexSha256": index_hash,
        "stimulusCount": len(analyzed),
        "thirdOctaveCentersHz": [float(value) for value in centers],
        "thirdOctaveMeanPsd": [float(value) for value in bands],
        "thirdOctaveRelativeDb": [float(value) for value in relative_db],
        "minimumSpeechRmsDbfs": min(float(item["speechRmsDbfs"]) for item in analyzed),
        "maximumSpeechRmsDbfs": max(float(item["speechRmsDbfs"]) for item in analyzed),
        "minimumActiveRegionSeconds": min(float(item["activeRegionSeconds"]) for item in analyzed),
        "maximumActiveRegionSeconds": max(float(item["activeRegionSeconds"]) for item in analyzed),
    }
    return metadata, analyzed


def generate_profile(repository: Path, output_root: Path, material_id: str, tool_hash: str) -> tuple[dict[str, object], dict[str, object]]:
    metadata, items = load_and_analyze_pack(repository, material_id)
    centers = np.asarray(metadata["thirdOctaveCentersHz"], dtype=np.float64)
    bands = np.asarray(metadata["thirdOctaveMeanPsd"], dtype=np.float64)
    filters: list[dict[str, object]] = []
    spectrum_validations: list[dict[str, object]] = []
    mix_validations: list[dict[str, object]] = []
    for sample_rate in OUTPUT_RATES:
        impulse = design_fir(sample_rate, centers, bands)
        coefficient_bytes = impulse.astype("<f8", copy=False).tobytes()
        filters.append({
            "sampleRate": sample_rate,
            "tapCount": FIR_TAPS,
            "groupDelaySamples": (FIR_TAPS - 1) // 2,
            "coefficientEncoding": "IEEE-754 float64 little-endian values serialized as JSON numbers",
            "coefficientSha256": hashlib.sha256(coefficient_bytes).hexdigest(),
            "coefficients": [float(value) for value in impulse],
        })
        spectrum_validations.append(validate_spectrum(impulse, sample_rate, centers, bands))
        mix_validations.append(validate_all_mix_boundaries(items, impulse, sample_rate))

    profile = {
        "schemaVersion": PROFILE_SCHEMA_VERSION,
        "protocolId": PROTOCOL_ID,
        "protocolVersion": PROTOCOL_VERSION,
        "material": {
            "id": metadata["materialId"],
            "stimulusCount": metadata["stimulusCount"],
            "catalogSha256": metadata["catalogSha256"],
            "audioIndexSha256": metadata["audioIndexSha256"],
        },
        "analysis": {
            "algorithm": "equal-item-welch-power-v1",
            "smoothing": "third-octave-log-power-v1",
            "sampleRate": ANALYSIS_RATE,
            "frameMilliseconds": FRAME_MS,
            "frameSamples": FRAME_SAMPLES,
            "hopMilliseconds": HOP_MS,
            "hopSamples": HOP_SAMPLES,
            "fftSize": FFT_SIZE,
            "window": "symmetric-hann",
            "activeFrameAbsoluteThresholdDbfs": ACTIVE_ABSOLUTE_DBFS,
            "activeFrameRelativeThresholdDb": ACTIVE_RELATIVE_DB,
            "speechRmsWindow": "first-to-last-active-frame-v1",
            "thirdOctaveCentersHz": metadata["thirdOctaveCentersHz"],
            "thirdOctaveRelativeDb": metadata["thirdOctaveRelativeDb"],
        },
        "filterDesign": {
            "algorithm": "frequency-sampling-blackman-v1",
            "targetBandHz": [int(LOWER_HZ), int(UPPER_HZ)],
            "tapCount": FIR_TAPS,
            "filters": filters,
        },
        "generator": {
            "algorithm": "xorshift32-fir-v1",
            "zeroSeedReplacementHex": "9e3779b9",
            "preRollSeconds": PRE_ROLL_SECONDS,
            "postRollSeconds": POST_ROLL_SECONDS,
            "edgeFadeSeconds": FADE_SECONDS,
        },
        "tool": {
            "name": Path(__file__).name,
            "version": TOOL_VERSION,
            "sha256": tool_hash,
            "python": platform.python_version(),
            "numpy": np.__version__,
        },
    }
    profile_path = output_root / material_id / "profile.json"
    write_json(profile_path, profile)
    profile_hash = sha256_file(profile_path)
    report = {
        "materialId": material_id,
        "profilePath": profile_path.relative_to(repository).as_posix(),
        "profileSha256": profile_hash,
        "material": metadata,
        "spectrumValidation": spectrum_validations,
        "mixBoundaryValidation": mix_validations,
        "validationResult": "passed",
        "notes": [
            "No audio endpoint was opened and no physical playback occurred.",
            "Full-scale violations are expected to be blocked, not normalized.",
            "Perceptual masking, item equality and test-retest reliability remain unvalidated.",
        ],
    }
    for item in items:
        del item["samples"]
    return profile, report


def markdown_report(reports: list[dict[str, object]]) -> str:
    lines = [
        "# Validierung: sprachangepasstes Kardinalzahlrauschen v1",
        "",
        "Der Offline-Generator hat beide materialgebundenen Profile ohne Audioausgabe erzeugt.",
        "Alle 900 Mess-WAVs pro Stimme wurden gegen Audioindex, SHA-256, PCM16, Mono und",
        "22,05 kHz geprüft. Die folgenden Werte beschreiben ausschließlich digitale Signale.",
        "Sie sind weder dB SPL noch eine klinische oder perzeptive Validierung.",
        "",
    ]
    for report in reports:
        material = report["material"]
        lines.extend([
            f"## {report['materialId']}",
            "",
            f"- Profil: `{report['profilePath']}`",
            f"- Profil-SHA-256: `{report['profileSha256']}`",
            f"- Stimuli: {material['stimulusCount']}",
            f"- Sprach-RMS: {material['minimumSpeechRmsDbfs']:.3f} bis {material['maximumSpeechRmsDbfs']:.3f} dBFS",
            f"- Aktiver Bereich: {material['minimumActiveRegionSeconds']:.3f} bis {material['maximumActiveRegionSeconds']:.3f} s",
            "",
            "| Ausgaberate | max. Terzbandabweichung | max. SNR-Fehler | Grenzfälle | davon Vollpegel-blockiert |",
            "| ---: | ---: | ---: | ---: | ---: |",
        ])
        spectrum_by_rate = {entry["sampleRate"]: entry for entry in report["spectrumValidation"]}
        for mix in report["mixBoundaryValidation"]:
            spectrum = spectrum_by_rate[mix["sampleRate"]]
            lines.append(
                f"| {mix['sampleRate']} Hz | {spectrum['maximumThirdOctaveDeviationDb']:.3f} dB | "
                f"{mix['maximumSnrErrorDb']:.6f} dB | {mix['renderCaseCount']} | "
                f"{mix['blockedForFullScaleCaseCount']} |"
            )
        lines.extend([
            "",
            "Vollpegelüberschreitungen wurden bewusst nicht normalisiert. Sie markieren Kombinationen,",
            "die ein späterer Renderer gemäß Vertrag blockieren muss.",
            "",
        ])
    lines.extend([
        "## Noch nicht abgenommen",
        "",
        "- keine physische Wiedergabe und keine Hörprüfung;",
        "- keine Freigabe der Kardinalzahlmessung im Rauschen in der App;",
        "- keine Aussage zu Maskierungswirkung, Item-/Listenäquivalenz oder Wiederholbarkeit.",
        "",
    ])
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repository", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    repository = args.repository.resolve()
    output_root = repository / "stimuli" / "de-DE" / "cardinal-speech-shaped-noise-v1"
    report_root = repository / "docs" / "reports" / "cardinal-speech-shaped-noise-v1"
    tool_hash = sha256_file(Path(__file__).resolve())
    reports: list[dict[str, object]] = []
    for material_id in MATERIAL_IDS:
        print(f"Analysiere {material_id} ...", flush=True)
        _, report = generate_profile(repository, output_root, material_id, tool_hash)
        reports.append(report)
        print(f"Validiert {material_id}.", flush=True)

    write_json(report_root / "validation.json", {
        "protocolId": PROTOCOL_ID,
        "protocolVersion": PROTOCOL_VERSION,
        "toolSha256": tool_hash,
        "reports": reports,
    })
    (report_root / "README.md").write_text(markdown_report(reports), encoding="utf-8", newline="\n")
    print(f"Profile: {output_root}")
    print(f"Bericht: {report_root}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
