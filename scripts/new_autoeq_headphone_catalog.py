#!/usr/bin/env python3
"""Build the bundled diffuse-field headphone equalization catalog from AutoEq.

The tool is offline apart from reading a local AutoEq clone. It reads everything
from the git object database of one pinned AutoEq commit (``git ls-tree``/
``git cat-file``), so Windows path-length limits of a checkout cannot silently
drop entries, and runs that commit's own AutoEq code from a temporary copy.

Selection for the hearing test (see hardware/headphone-equalization/README):
1. Only over-ear results. In-ears, CIEMs, APEX modules and earbuds cannot be
   worn together with a hearing aid on the tested ear.
2. Models with several measurements keep only the one AutoEq recommends in
   results/README.md.
3. Wireless, Bluetooth and active noise-cancelling models are dropped unless
   the measurement is explicitly passive or wired: their own DSP, volume and
   ANC make the level uncontrollable and the measured mode is unknown.
4. Variants of one model (parenthesised year, earpads, filters, ANC modes,
   switch positions, samples) collapse to one entry. Impedance, generation and
   version markers stay part of the model identity. The preferred variant is
   the plain name, then a passive/wired/ANC-off mode, then a stock/default one.

Equalization: instead of AutoEq's default Harman results, every selected model
is equalized anew to a diffuse-field target without bass boost, with AutoEq's
own pipeline and the parameters AutoEq uses for its results (default smoothing
and gain limits, 4 peaking + low shelf and 4 peaking + high shelf, 44.1 kHz).
The diffuse-field target depends on the measurement rig:

- GRAS 43AG-7 and compatible couplers: AutoEq "Diffuse field GRAS KEMAR".
- Bruel & Kjaer 5128: AutoEq "Diffuse field 5128".
- HMS II.3 and EARS + 711 have no AutoEq diffuse-field target. They get the
  GRAS KEMAR diffuse field plus the rig adaptation AutoEq applies to Harman for
  that rig (rig Harman target minus GRAS Harman target).

The input is the raw measurement from ``measurements/``. crinacle publishes no
raw data; for those models the measurement is reconstructed from AutoEq's
GraphicEQ result as Harman target (with AutoEq's 6 dB bass boost) minus
equalization. Max-gain clipping of the Harman result is lost in that
reconstruction, so these entries are marked ``basis: graphicEq``.

Requires the Python packages AutoEq pins (Python 3.8-3.11), for example:
    uv run --no-project --python 3.11 --with numpy~=1.24.4 --with scipy~=1.10.1 \
        --with matplotlib~=3.7.3 --with Pillow~=10.0.1 --with tabulate~=0.9.0 \
        --with soundfile~=0.12.1 --with pyyaml~=6.0 --with tqdm~=4.66.1 \
        python scripts/new_autoeq_headphone_catalog.py --autoeq-repo autoeq

Clone:
    git clone --filter=blob:none --no-checkout https://github.com/jaakkopasanen/AutoEq.git autoeq
"""

from __future__ import annotations

import argparse
import hashlib
import json
from multiprocessing import Pool
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import threading
from urllib.parse import unquote


CATALOG_ID = "autoeq-diffuse-field-parametric-eq"
DEFAULT_COMMIT = "7ae0f56d53074872b028649617a22bbb4232feb7"
SOURCE_REPOSITORY = "https://github.com/jaakkopasanen/AutoEq"
TARGET_DESCRIPTION = "Diffusfeld ohne Bassanhebung, an den Messaufbau angepasst (je Eintrag angegeben)"
SUFFIX = " ParametricEQ.txt"
GRAPHIC_SUFFIX = " GraphicEQ.txt"
SAMPLE_RATE = 44100
# Wie AutoEqs update_results in dbtools/db.ipynb.
PEQ_CONFIG_NAMES = ("4_PEAKING_WITH_LOW_SHELF", "4_PEAKING_WITH_HIGH_SHELF")
BASS_BOOST_FC = 105.0
BASS_BOOST_Q = 0.7
HARMAN_GRAS = "Harman over-ear 2018 without bass"
DF_GRAS = "Diffuse field GRAS KEMAR"
DF_5128 = "Diffuse field 5128"
# Messaufbau-Familie je (Quelle, Messaufbau) und das Harman-Target, mit dem AutoEq sie entzerrt hat
# (dbtools/db.ipynb und dbtools/create_webapp_data.py des Commits).
RIG_FAMILIES = {
    "gras": {
        "harman": HARMAN_GRAS,
        "target": "diffuse-field-gras-kemar",
        "description": "Diffusfeld GRAS KEMAR (AutoEq), ohne Bassanhebung",
    },
    "5128": {
        "harman": "LMG 5128 0.6 without bass",
        "target": "diffuse-field-bk-5128",
        "description": "Diffusfeld B&K 5128 (AutoEq), ohne Bassanhebung",
    },
    "hms": {
        "harman": "HMS II.3 Harman over-ear 2018 without bass",
        "target": "diffuse-field-hms-ii3",
        "description": "Diffusfeld GRAS KEMAR mit AutoEqs HMS-II.3-Anpassung, ohne Bassanhebung",
    },
    "ears": {
        "harman": "crinacle EARS + 711 Harman over-ear 2018 without bass",
        "target": "diffuse-field-ears-711",
        "description": "Diffusfeld GRAS KEMAR mit AutoEqs EARS-+-711-Anpassung, ohne Bassanhebung",
    },
}
RIG_FAMILY_BY_SOURCE_RIG = {
    ("Auriculares Argentina", "over-ear"): "gras",
    ("crinacle", "GRAS 43AG-7 over-ear"): "gras",
    ("Filk", "over-ear"): "gras",
    ("kr0mka", "over-ear"): "gras",
    ("Kuulokenurkka", "over-ear"): "gras",
    ("oratory1990", "over-ear"): "gras",
    ("Regan Cipher", "over-ear"): "gras",
    ("Super Review", "over-ear"): "gras",
    ("HypetheSonics", "over-ear"): "5128",
    ("Rtings", "Bruel & Kjaer 5128 over-ear"): "5128",
    ("Headphone.com Legacy", "over-ear"): "hms",
    ("Innerfidelity", "over-ear"): "hms",
    ("Rtings", "HMS II.3 over-ear"): "hms",
    ("crinacle", "EARS + 711 over-ear"): "ears",
}
PREAMP = re.compile(r"^Preamp: (-?\d+(?:\.\d+)?) dB$")
FILTER = re.compile(
    r"^Filter (\d+): ON (PK|LSC|HSC) Fc (\d+(?:\.\d+)?) Hz Gain (-?\d+(?:\.\d+)?) dB Q (\d+(?:\.\d+)?)$"
)
RECOMMENDED_LINK = re.compile(r"^- \[.*\]\(\./(.+)\)$")
SUITABLE_FORMS = {"over-ear"}
PARENTHESES = re.compile(r"\s*\(([^)]*)\)")
# Klammerinhalte, die ein eigenes Produkt bezeichnen und daher zur Modellidentität gehören.
IDENTITY_MARKER = re.compile(r"\b(\d+\s*ohms?|gen(eration)?\s*\d+|v\d+(\.\d+)?|mk\s*[ivx\d]+)\b", re.IGNORECASE)
# Aktive Kopfhörer (Funk, Bluetooth, ANC); nur ausdrücklich passive oder kabelgebundene Messungen bleiben.
ACTIVE_MODEL = re.compile(
    r"wireless|bluetooth|\bBT\b|\bANC\b|\bNC\b|\d+\s*NC\b|\d+NB\b|noise[- ]cancel|quietcomfort|\bWH-|MDR-1000X|airpods max",
    re.IGNORECASE)
PASSIVE_MEASUREMENT = re.compile(r"\bpassive\b|\bwired\b|\banalog", re.IGNORECASE)
ACTIVE_MEASUREMENT = re.compile(r"\banc on\b|\bactive\b|\bwireless\b|bluetooth|\bpower on\b", re.IGNORECASE)
# Bevorzugte Variante, wenn kein Eintrag ohne Variantenangabe existiert: passiver, kabelgebundener Betrieb zuerst.
PREFERRED_VARIANTS = [
    re.compile(pattern, re.IGNORECASE)
    for pattern in (
        r"\bpassive\b", r"\bwired\b", r"\banalog", r"\banc off\b", r"\bstock\b",
        r"\bdefault\b", r"\bstandard\b", r"\bfresh\b", r"\breference\b", r"\bflat\b",
    )
]


def git(repo: Path, *args: str) -> bytes:
    return subprocess.run(["git", "-C", str(repo), *args], check=True, capture_output=True).stdout


def prefetch_blobs(repo: Path, object_ids: list[str]) -> None:
    # Ein Blobless-Klon lädt fehlende Blobs sonst einzeln nach (je ein Netzwerkabruf); daher gesammelt holen.
    check = subprocess.run(
        ["git", "-C", str(repo), "cat-file", "--batch-check"],
        input="".join(f"{object_id}\n" for object_id in object_ids).encode("ascii"),
        capture_output=True, check=True, env={**os.environ, "GIT_NO_LAZY_FETCH": "1"})
    missing = [line.split()[0] for line in check.stdout.decode("ascii").splitlines() if line.endswith(" missing")]
    remote = git(repo, "config", "--get-regexp", r"^remote\..*\.promisor$").decode("ascii").split()[0] \
        .removeprefix("remote.").removesuffix(".promisor") if missing else None
    for start in range(0, len(missing), 500):
        git(repo, "-c", "fetch.negotiationAlgorithm=noop", "fetch", "--no-tags", "--no-write-fetch-head",
            "--filter=blob:none", remote, *missing[start:start + 500])


def read_blobs(repo: Path, object_ids: list[str]) -> dict[str, bytes]:
    prefetch_blobs(repo, object_ids)
    process = subprocess.Popen(
        ["git", "-C", str(repo), "cat-file", "--batch"],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
    )
    assert process.stdin and process.stdout

    def write_requests() -> None:
        # Getrennter Schreibthread: cat-file blockiert sonst bei vollem Ausgabepuffer.
        process.stdin.write("".join(f"{object_id}\n" for object_id in object_ids).encode("ascii"))
        process.stdin.close()

    writer = threading.Thread(target=write_requests)
    writer.start()
    blobs: dict[str, bytes] = {}
    for object_id in object_ids:
        header = process.stdout.readline().decode("ascii").split()
        if len(header) != 3 or header[0] != object_id or header[1] != "blob":
            raise RuntimeError(f"Unerwartete cat-file-Antwort für {object_id}: {header}")
        blobs[object_id] = process.stdout.read(int(header[2]))
        process.stdout.read(1)
    writer.join()
    if process.wait() != 0:
        raise RuntimeError("git cat-file ist fehlgeschlagen.")
    return blobs


def validate_text(path: str, text: str) -> None:
    lines = text.splitlines()
    if not lines or not PREAMP.match(lines[0]):
        raise ValueError(f"{path}: Preamp-Zeile fehlt.")
    filters = lines[1:]
    if not filters:
        raise ValueError(f"{path}: keine Filter.")
    for number, line in enumerate(filters, start=1):
        match = FILTER.match(line)
        if not match or int(match.group(1)) != number:
            raise ValueError(f"{path}: ungültige Filterzeile '{line}'.")


def model_key(name: str) -> str:
    # Von Klammern zählt nur eine Impedanz-/Generations-/Versionsmarkierung selbst zur Identität.
    identity = PARENTHESES.sub(
        lambda match: " " + " ".join(marker.group(0) for marker in IDENTITY_MARKER.finditer(match.group(1))),
        name)
    return re.sub(r"[\s\-_]", "", identity).casefold()


def is_active_without_passive_measurement(name: str) -> bool:
    if not ACTIVE_MODEL.search(name):
        return False
    variants = ", ".join(PARENTHESES.findall(name))
    return not (PASSIVE_MEASUREMENT.search(variants) and not ACTIVE_MEASUREMENT.search(variants))


def variant_rank(name: str) -> tuple:
    # Variantenangabe ist, was nach Entfernen der Identitätsmarkierungen in den Klammern übrig bleibt.
    variants = [
        remainder
        for text in PARENTHESES.findall(name)
        if (remainder := IDENTITY_MARKER.sub("", text).strip(" ,;-"))
    ]
    if not variants:
        preference = 0
    else:
        text = ", ".join(variants)
        preference = next(
            (index + 1 for index, pattern in enumerate(PREFERRED_VARIANTS) if pattern.search(text)),
            len(PREFERRED_VARIANTS) + 1)
    # Bei gleichwertigen Schreibweisen („HD800“/„HD 800“) die lesbarere mit mehr Leerzeichen.
    return (preference, -name.count(" "), len(name), name.casefold())


def form_of(rig: str) -> str:
    for form in ("over-ear", "in-ear", "earbud"):
        if rig == form or rig.endswith(" " + form):
            return form
    raise ValueError(f"Unbekannte Bauform im Messaufbau '{rig}'.")


def measurement_path(paths: set[str], source: str, rig: str, name: str) -> str | None:
    # AutoEq führt die Rtings-Messungen teils unter „rtings“; je Modell existiert genau eine der Schreibweisen.
    rig_dir = "" if rig in SUITABLE_FORMS else rig.removesuffix(" over-ear") + "/"
    candidates = {f"measurements/{folder}/data/over-ear/{rig_dir}{name}.csv" for folder in (source, source.lower())}
    found = sorted(candidates & paths)
    if len(found) > 1:
        raise ValueError(f"{source}/{rig}/{name}: mehrere Rohmessungen {found}.")
    return found[0] if found else None


def write_target_csv(path: Path, frequencies, values) -> bytes:
    lines = ["frequency,raw"] + [f"{frequency:.2f},{value:.2f}" for frequency, value in zip(frequencies, values)]
    data = ("\n".join(lines) + "\n").encode("utf-8")
    path.write_bytes(data)
    return data


_worker: dict = {}


def init_worker(autoeq_root: str, target_paths: dict[str, str]) -> None:
    # Läuft in jedem Prozess (Windows startet Worker frisch): AutoEq-Code des Commits laden, Targets wie
    # batch_processing vorbereiten (interpolieren, bei 1 kHz zentrieren).
    sys.path.insert(0, autoeq_root)
    from autoeq.constants import DEFAULT_BASS_BOOST_GAINS, PEQ_CONFIGS
    from autoeq.frequency_response import FrequencyResponse

    targets = {}
    for key, path in target_paths.items():
        target = FrequencyResponse.read_csv(path)
        target.interpolate()
        target.center()
        targets[key] = target
    _worker.update(
        FrequencyResponse=FrequencyResponse,
        bass_boost_gains=DEFAULT_BASS_BOOST_GAINS,
        peq_configs=[PEQ_CONFIGS[name] for name in PEQ_CONFIG_NAMES],
        targets=targets)


def reconstruct_from_graphic_eq(graphic_text: str, harman_name: str):
    # Rohmessung ≈ Harman-Target mit AutoEqs Bassanhebung minus Entzerrung; der konstante Versatz der
    # normalisierten GraphicEQ verschwindet beim Zentrieren.
    FrequencyResponse = _worker["FrequencyResponse"]
    if not graphic_text.startswith("GraphicEQ: "):
        raise ValueError("Keine GraphicEQ-Datei.")
    points = [item.split() for item in graphic_text[len("GraphicEQ: "):].strip().split(";")]
    equalization = FrequencyResponse(
        name="graphic", frequency=[float(f) for f, _ in points], raw=[float(g) for _, g in points])
    equalization.interpolate()
    harman = _worker["targets"][harman_name]
    if len(harman.frequency) != len(equalization.frequency) or any(harman.frequency != equalization.frequency):
        raise ValueError("Frequenzraster von Target und GraphicEQ weichen ab.")
    harman_with_bass = harman.raw + harman.create_target(
        bass_boost_gain=_worker["bass_boost_gains"][harman_name], bass_boost_fc=BASS_BOOST_FC, bass_boost_q=BASS_BOOST_Q)
    return FrequencyResponse(name="reconstructed", frequency=harman.frequency.copy(), raw=harman_with_bass - equalization.raw)


def equalize(job: tuple[str, str, str, str]) -> tuple[str, str]:
    import copy

    entry_id, family, basis, basis_text = job
    FrequencyResponse = _worker["FrequencyResponse"]
    with tempfile.TemporaryDirectory() as directory:
        if basis == "measurement":
            path = os.path.join(directory, "measurement.csv")
            Path(path).write_text(basis_text, encoding="utf-8")
            response = FrequencyResponse.read_csv(path)
        else:
            response = reconstruct_from_graphic_eq(basis_text, RIG_FAMILIES[family]["harman"])
        response.process(
            target=copy.deepcopy(_worker["targets"][RIG_FAMILIES[family]["target"]]),
            min_mean_error=True,
            bass_boost_gain=0.0,
            bass_boost_fc=BASS_BOOST_FC,
            bass_boost_q=BASS_BOOST_Q,
            fs=SAMPLE_RATE)
        peqs = response.optimize_parametric_eq(_worker["peq_configs"], SAMPLE_RATE)
        output = os.path.join(directory, "ParametricEQ.txt")
        response.write_eqapo_parametric_eq(output, peqs)
        return entry_id, Path(output).read_text(encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--autoeq-repo", type=Path, required=True)
    parser.add_argument("--commit", default=DEFAULT_COMMIT)
    parser.add_argument("--output-root", type=Path, default=Path("hardware/headphone-equalization"))
    parser.add_argument("--jobs", type=int, default=os.cpu_count())
    arguments = parser.parse_args()

    repo = arguments.autoeq_repo
    commit = git(repo, "rev-parse", arguments.commit).decode("ascii").strip()
    commit_date = git(repo, "show", "-s", "--format=%cI", commit).decode("ascii").strip()

    tree: dict[str, str] = {}
    for record in git(repo, "ls-tree", "-r", "-z", commit, "results", "measurements", "autoeq", "targets").split(b"\0"):
        if not record:
            continue
        meta, path = record.split(b"\t", 1)
        _, kind, object_id = meta.decode("ascii").split()
        if kind == "blob":
            tree[path.decode("utf-8")] = object_id
    # Die Auswahl richtet sich nach den vorhandenen AutoEq-Ergebnissen; deren Harman-Parameter werden nicht gelesen.
    files = [path for path in tree if path.startswith("results/") and path.endswith(SUFFIX)]
    measurement_paths = {path for path in tree if path.startswith("measurements/") and path.endswith(".csv")}

    readme_id = git(repo, "rev-parse", f"{commit}:results/README.md").decode("ascii").strip()
    license_id = git(repo, "rev-parse", f"{commit}:LICENSE").decode("ascii").strip()
    blobs = read_blobs(repo, [readme_id, license_id])

    recommended: set[str] = set()
    for line in blobs[readme_id].decode("utf-8").splitlines():
        match = RECOMMENDED_LINK.match(line.strip())
        if match:
            recommended.add(unquote(match.group(1)).rstrip("/"))

    entries = []
    for path in files:
        parts = path.split("/")
        if len(parts) != 5 or parts[3] + SUFFIX != parts[4]:
            raise ValueError(f"Unerwartete Ergebnisstruktur: {path}")
        _, source, rig, name, _ = parts
        entry_id = f"{source}/{rig}/{name}"
        entries.append({
            "id": entry_id,
            "name": name,
            "source": source,
            "rig": rig,
            "form": form_of(rig),
            "recommended": entry_id in recommended,
        })

    total_results = len(entries)
    entries = [entry for entry in entries if entry["form"] in SUITABLE_FORMS]

    # Je Modell nur eine Messung: Bei mehreren Messungen bleibt ausschließlich die von AutoEq empfohlene.
    by_name: dict[str, list[dict]] = {}
    for entry in entries:
        by_name.setdefault(entry["name"], []).append(entry)
    entries = []
    for name, candidates in by_name.items():
        if len(candidates) == 1:
            entries.extend(candidates)
            continue
        recommended_candidates = [entry for entry in candidates if entry["recommended"]]
        if len(recommended_candidates) != 1:
            raise ValueError(f"{name}: {len(recommended_candidates)} empfohlene unter {len(candidates)} Messungen.")
        entries.extend(recommended_candidates)

    entries = [entry for entry in entries if not is_active_without_passive_measurement(entry["name"])]

    # Varianten eines Modells auf einen Eintrag zusammenfassen.
    by_model: dict[str, list[dict]] = {}
    for entry in entries:
        by_model.setdefault(model_key(entry["name"]), []).append(entry)
    entries = [min(candidates, key=lambda entry: variant_rank(entry["name"])) for candidates in by_model.values()]

    entries.sort(key=lambda entry: (entry["name"].casefold(), entry["source"].casefold(), entry["rig"].casefold()))
    ids = [entry["id"] for entry in entries]
    if len(ids) != len(set(ids)):
        raise ValueError("Doppelte Katalog-IDs.")

    # Grundlage je Eintrag: Rohmessung, sonst (nur crinacle) AutoEqs GraphicEQ-Ergebnis.
    for entry in entries:
        family = RIG_FAMILY_BY_SOURCE_RIG.get((entry["source"], entry["rig"]))
        if family is None:
            raise ValueError(f"{entry['id']}: Messaufbau ohne Diffusfeld-Zuordnung.")
        entry["family"] = family
        path = measurement_path(measurement_paths, entry["source"], entry["rig"], entry["name"])
        if path is not None:
            entry["basis"] = "measurement"
        elif entry["source"] == "crinacle":
            entry["basis"] = "graphicEq"
            path = f"results/{entry['id']}/{entry['name']}{GRAPHIC_SUFFIX}"
            if path not in tree:
                raise ValueError(f"{entry['id']}: weder Rohmessung noch GraphicEQ.")
        else:
            raise ValueError(f"{entry['id']}: Rohmessung fehlt.")
        entry["basisPath"] = path

    target_files = {name: f"targets/{name}.csv" for name in
                    {HARMAN_GRAS, DF_GRAS, DF_5128, *(family["harman"] for family in RIG_FAMILIES.values())}}
    basis_blobs = read_blobs(
        repo,
        sorted({tree[entry["basisPath"]] for entry in entries}
               | {tree[path] for path in target_files.values()}
               | {object_id for path, object_id in tree.items() if path.startswith("autoeq/")}))

    output = arguments.output_root / f"autoeq-{commit[:7]}-diffuse-field"
    targets_output = output / "targets"
    if targets_output.exists():
        shutil.rmtree(targets_output)
    targets_output.mkdir(parents=True)

    with tempfile.TemporaryDirectory() as work:
        work_path = Path(work)
        for path, object_id in tree.items():
            if path.startswith("autoeq/"):
                destination = work_path / path
                destination.parent.mkdir(parents=True, exist_ok=True)
                destination.write_bytes(basis_blobs[object_id])
        source_targets = {}
        for name, path in target_files.items():
            destination = work_path / path
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(basis_blobs[tree[path]])
            source_targets[name] = str(destination)

        # Wirksame Diffusfeld-Targets: AutoEq-Dateien unverändert, abgeleitete auf AutoEqs Standardraster.
        init_worker(str(work_path), source_targets)
        prepared = _worker["targets"]
        derived_from = {"hms": RIG_FAMILIES["hms"]["harman"], "ears": RIG_FAMILIES["ears"]["harman"]}
        catalog_targets = []
        effective_paths = dict(source_targets)
        for key, family in RIG_FAMILIES.items():
            target_path = targets_output / f"{family['target']}.csv"
            if key == "gras":
                data = basis_blobs[tree[target_files[DF_GRAS]]]
                target_path.write_bytes(data)
                derivation = f"AutoEq targets/{DF_GRAS}.csv, unverändert"
            elif key == "5128":
                data = basis_blobs[tree[target_files[DF_5128]]]
                target_path.write_bytes(data)
                derivation = f"AutoEq targets/{DF_5128}.csv, unverändert"
            else:
                rig_harman = derived_from[key]
                values = prepared[DF_GRAS].raw + prepared[rig_harman].raw - prepared[HARMAN_GRAS].raw
                data = write_target_csv(target_path, prepared[DF_GRAS].frequency, values)
                derivation = f"{DF_GRAS} + ({rig_harman} - {HARMAN_GRAS}), je bei 1 kHz zentriert"
            effective_paths[family["target"]] = str(target_path)
            catalog_targets.append({
                "id": family["target"],
                "description": family["description"],
                "file": f"targets/{family['target']}.csv",
                "derivation": derivation,
                "sha256": hashlib.sha256(data).hexdigest(),
            })

        jobs = [
            (entry["id"], entry["family"], entry["basis"], basis_blobs[tree[entry["basisPath"]]].decode("utf-8"))
            for entry in entries
        ]
        with Pool(arguments.jobs, initializer=init_worker, initargs=(str(work_path), effective_paths)) as pool:
            texts = dict(pool.imap_unordered(equalize, jobs, chunksize=4))

    catalog_entries = []
    for entry in entries:
        text = texts[entry["id"]]
        validate_text(entry["id"], text)
        family = RIG_FAMILIES[entry["family"]]
        catalog_entries.append({
            "id": entry["id"],
            "name": entry["name"],
            "source": entry["source"],
            "rig": entry["rig"],
            "form": entry["form"],
            "recommended": entry["recommended"],
            "targetId": family["target"],
            "target": family["description"],
            "basis": entry["basis"],
            "basisPath": entry["basisPath"],
            "basisSha256": hashlib.sha256(basis_blobs[tree[entry["basisPath"]]]).hexdigest(),
            "sha256": hashlib.sha256(text.encode("utf-8")).hexdigest(),
            "text": text,
        })

    catalog = {
        "catalogId": CATALOG_ID,
        "catalogVersion": commit[:7],
        "sourceRepository": SOURCE_REPOSITORY,
        "sourceCommit": commit,
        "sourceCommitDate": commit_date,
        "license": "MIT, Copyright (c) 2018-2022 Jaakko Pasanen; siehe LICENSE",
        "target": TARGET_DESCRIPTION,
        "processing": "AutoEq-Code des Commits: FrequencyResponse.process mit Standardglättung und -begrenzung, "
                      "min_mean_error, Bassanhebung 0 dB; Parametrik 4_PEAKING_WITH_LOW_SHELF + "
                      "4_PEAKING_WITH_HIGH_SHELF bei 44,1 kHz",
        "generator": "scripts/new_autoeq_headphone_catalog.py",
        "selection": "Nur over-ear; je Modell eine Messung (AutoEq-Empfehlung); Funk-/Bluetooth-/ANC-Modelle nur "
                     "mit ausdrücklich passiver oder kabelgebundener Messung; Varianten zusammengefasst, bevorzugt "
                     "ohne Variantenangabe, sonst passiv/kabelgebunden",
        "sourceResultCount": total_results,
        "entryCount": len(catalog_entries),
        "targets": catalog_targets,
        "entries": catalog_entries,
    }
    (output / "catalog.json").write_text(
        json.dumps(catalog, ensure_ascii=False, indent=1) + "\n", encoding="utf-8", newline="\n")
    (output / "LICENSE").write_bytes(blobs[license_id])
    reconstructed = sum(entry["basis"] == "graphicEq" for entry in catalog_entries)
    print(f"{len(catalog_entries)} von {total_results} Ergebnissen neu entzerrt "
          f"({reconstructed} aus GraphicEQ rekonstruiert) -> {output}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
