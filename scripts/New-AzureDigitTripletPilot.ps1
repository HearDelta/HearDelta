[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\diagnostics\audio\azure-digit-triplet-pilot-2026-09-08'),

    [string]$VoiceName = 'de-DE-RalfNeural',

    [ValidateRange(-50, 100)]
    [int]$RatePercent = -30,

    [ValidateRange(100, 1000)]
    [int]$InterDigitPauseMilliseconds = 400,

    [switch]$ForceApi
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$subscriptionKey = [Environment]::GetEnvironmentVariable('AZURE_SPEECH_KEY')
if ([string]::IsNullOrWhiteSpace($subscriptionKey)) {
    throw 'AZURE_SPEECH_KEY ist nicht gesetzt.'
}
$speechRegion = [Environment]::GetEnvironmentVariable('AZURE_SPEECH_REGION')
if ([string]::IsNullOrWhiteSpace($speechRegion) -or $speechRegion -notmatch '^[a-z0-9]+$') {
    throw 'AZURE_SPEECH_REGION fehlt oder hat ein unerwartetes Format.'
}

try {
    $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    function Resolve-RequiredTool([string]$Name) {
        $command = Get-Command $Name -ErrorAction SilentlyContinue
        if ($null -ne $command) {
            return $command
        }

        $wingetPackages = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
        $candidate = Get-ChildItem -LiteralPath $wingetPackages -Filter "$Name.exe" -File -Recurse `
            -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $candidate) {
            throw "Erforderliches Werkzeug fehlt: $Name."
        }
        return [pscustomobject]@{ Source = $candidate.FullName }
    }

    $ffmpeg = Resolve-RequiredTool 'ffmpeg'
    $ffprobe = Resolve-RequiredTool 'ffprobe'
    $utf8 = [Text.UTF8Encoding]::new($false)
    $invariantCulture = [Globalization.CultureInfo]::InvariantCulture
    $targetSampleRate = 22050
    $targetPeakDbfs = -6.0
    $azureOutputFormat = 'riff-24khz-16bit-mono-pcm'
    $ttsEndpoint = "https://${speechRegion}.tts.speech.microsoft.com/cognitiveservices/v1"
    $rateValue = if ($RatePercent -gt 0) { "+${RatePercent}%" } else { "${RatePercent}%" }

    $digitWords = [ordered]@{
        '0' = 'null'; '1' = 'eins'; '2' = 'zwei'; '3' = 'drei'; '4' = 'vier';
        '5' = 'fünf'; '6' = 'sechs'; '7' = 'sieben'; '8' = 'acht'; '9' = 'neun'
    }
    # The set deliberately includes every digit, ascending/descending order and repeats.
    $triplets = @(
        [ordered]@{ id = 'dt-000'; digits = @(0, 0, 0) },
        [ordered]@{ id = 'dt-111'; digits = @(1, 1, 1) },
        [ordered]@{ id = 'dt-222'; digits = @(2, 2, 2) },
        [ordered]@{ id = 'dt-345'; digits = @(3, 4, 5) },
        [ordered]@{ id = 'dt-987'; digits = @(9, 8, 7) },
        [ordered]@{ id = 'dt-404'; digits = @(4, 0, 4) },
        [ordered]@{ id = 'dt-440'; digits = @(4, 4, 0) },
        [ordered]@{ id = 'dt-707'; digits = @(7, 0, 7) },
        [ordered]@{ id = 'dt-190'; digits = @(1, 9, 0) },
        [ordered]@{ id = 'dt-618'; digits = @(6, 1, 8) },
        [ordered]@{ id = 'dt-253'; digits = @(2, 5, 3) },
        [ordered]@{ id = 'dt-876'; digits = @(8, 7, 6) }
    )

    function Get-Sha256([string]$Path) {
        (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    }

    function Invoke-AzureSpeech([string]$SpeechBody, [string]$DestinationPath) {
        $ssml = @"
<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="de-DE">
  <voice name="$VoiceName"><prosody rate="$rateValue">$SpeechBody</prosody></voice>
</speak>
"@
        $temporaryPath = "$DestinationPath.download"
        $headers = @{
            'Ocp-Apim-Subscription-Key' = $subscriptionKey
            'X-Microsoft-OutputFormat' = $azureOutputFormat
            'User-Agent' = 'HearDelta-AzureDigitTripletPilot'
        }
        try {
            for ($attempt = 1; $attempt -le 3; $attempt++) {
                try {
                    Invoke-WebRequest -Uri $ttsEndpoint -Method Post -Headers $headers `
                        -ContentType 'application/ssml+xml; charset=utf-8' -Body $utf8.GetBytes($ssml) `
                        -OutFile $temporaryPath | Out-Null
                    Move-Item -LiteralPath $temporaryPath -Destination $DestinationPath -Force
                    return
                }
                catch {
                    Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
                    if ($attempt -eq 3) { throw }
                    Start-Sleep -Seconds ([math]::Pow(2, $attempt))
                }
            }
        }
        finally {
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        }
    }

    function Assert-SourceFormat([string]$Path) {
        $metadata = (& $ffprobe.Source -v error -select_streams a:0 `
            -show_entries stream=codec_name,sample_rate,channels,bits_per_sample -of json $Path | ConvertFrom-Json).streams[0]
        if ($metadata.codec_name -ne 'pcm_s16le' -or [int]$metadata.sample_rate -ne 24000 -or
            [int]$metadata.channels -ne 1 -or [int]$metadata.bits_per_sample -ne 16) {
            throw "Unerwartetes Azure-Rohformat: $Path"
        }
    }

    function Convert-ToMeasurementFormat([string]$SourcePath, [string]$DestinationPath) {
        $temporaryPath = "$DestinationPath.pre-normalize.wav"
        try {
            & $ffmpeg.Source -nostdin -hide_banner -loglevel error -y -i $SourcePath `
                -ar $targetSampleRate -ac 1 -c:a pcm_s16le $temporaryPath
            if ($LASTEXITCODE -ne 0) { throw "Audio-Konvertierung fehlgeschlagen: $SourcePath" }
            $volumeOutput = & $ffmpeg.Source -nostdin -hide_banner -i $temporaryPath -af volumedetect -f null NUL 2>&1
            $peakMatch = [regex]::Match(($volumeOutput -join "`n"), 'max_volume:\s*(-?[0-9]+(?:\.[0-9]+)?)\s*dB')
            if (-not $peakMatch.Success) { throw "Spitzenpegel nicht bestimmbar: $SourcePath" }
            $gainDb = $targetPeakDbfs - [double]::Parse($peakMatch.Groups[1].Value, $invariantCulture)
            $gainText = $gainDb.ToString('0.00', $invariantCulture)
            & $ffmpeg.Source -nostdin -hide_banner -loglevel error -y -i $temporaryPath `
                -af "volume=${gainText}dB" -c:a pcm_s16le $DestinationPath
            if ($LASTEXITCODE -ne 0) { throw "Audio-Normalisierung fehlgeschlagen: $SourcePath" }
        }
        finally {
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        }
    }

    function Add-Preview([string]$Variant, [string[]]$SourcePaths) {
        $silencePath = Join-Path $OutputDirectory '_preview-silence.wav'
        $listPath = Join-Path $OutputDirectory "_preview-${Variant}.txt"
        try {
            & $ffmpeg.Source -nostdin -hide_banner -loglevel error -y -f lavfi `
                -i "anullsrc=r=${targetSampleRate}:cl=mono" -t 0.75 -c:a pcm_s16le $silencePath
            if ($LASTEXITCODE -ne 0) { throw 'Vorschausilenz konnte nicht erzeugt werden.' }
            $lines = foreach ($sourcePath in $SourcePaths) {
                "file '$($sourcePath.Replace("'", "''"))'"
                "file '$($silencePath.Replace("'", "''"))'"
            }
            [IO.File]::WriteAllLines($listPath, @($lines), $utf8)
            $previewPath = Join-Path $OutputDirectory "preview-${Variant}.wav"
            & $ffmpeg.Source -nostdin -hide_banner -loglevel error -y -f concat -safe 0 -i $listPath -c:a pcm_s16le $previewPath
            if ($LASTEXITCODE -ne 0) { throw "Vorschau fehlgeschlagen: $Variant" }
        }
        finally {
            Remove-Item -LiteralPath $silencePath -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $listPath -Force -ErrorAction SilentlyContinue
        }
    }

    foreach ($path in @(
        $OutputDirectory,
        (Join-Path $OutputDirectory 'raw\complete'),
        (Join-Path $OutputDirectory 'raw\digits'),
        (Join-Path $OutputDirectory 'raw\composed'),
        (Join-Path $OutputDirectory 'normalized\complete'),
        (Join-Path $OutputDirectory 'normalized\composed'))) {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }

    foreach ($digit in 0..9) {
        $rawPath = Join-Path $OutputDirectory "raw\digits\${digit}.wav"
        if ($ForceApi -or -not (Test-Path -LiteralPath $rawPath -PathType Leaf)) {
            Write-Host "Azure Ziffer: $($digitWords[$digit])"
            Invoke-AzureSpeech -SpeechBody $digitWords[$digit] -DestinationPath $rawPath
        }
        Assert-SourceFormat -Path $rawPath
    }

    $entries = [Collections.Generic.List[object]]::new()
    foreach ($triplet in $triplets) {
        $words = @($triplet.digits | ForEach-Object { $digitWords[$_] })
        $completeRawPath = Join-Path $OutputDirectory "raw\complete\$($triplet.id).wav"
        $composedRawPath = Join-Path $OutputDirectory "raw\composed\$($triplet.id).wav"
        $completePath = Join-Path $OutputDirectory "normalized\complete\$($triplet.id).wav"
        $composedPath = Join-Path $OutputDirectory "normalized\composed\$($triplet.id).wav"
        $body = ($words | ForEach-Object { "$_<break time=`"${InterDigitPauseMilliseconds}ms`"/>" }) -join ''

        if ($ForceApi -or -not (Test-Path -LiteralPath $completeRawPath -PathType Leaf)) {
            Write-Host "Azure vollständiges Tripel: $($triplet.id) ($($words -join ' - '))"
            Invoke-AzureSpeech -SpeechBody $body -DestinationPath $completeRawPath
        }
        Assert-SourceFormat -Path $completeRawPath

        if ($ForceApi -or -not (Test-Path -LiteralPath $composedRawPath -PathType Leaf)) {
            $concatListPath = Join-Path $OutputDirectory "raw\composed\$($triplet.id).txt"
            $silencePath = Join-Path $OutputDirectory "raw\composed\$($triplet.id)-silence.wav"
            try {
                & $ffmpeg.Source -nostdin -hide_banner -loglevel error -y -f lavfi `
                    -i 'anullsrc=r=24000:cl=mono' -t ($InterDigitPauseMilliseconds / 1000.0) -c:a pcm_s16le $silencePath
                if ($LASTEXITCODE -ne 0) { throw "Ziffernpause konnte nicht erzeugt werden: $($triplet.id)" }
                $sourcePaths = @($triplet.digits | ForEach-Object { Join-Path $OutputDirectory "raw\digits\${_}.wav" })
                $lines = @(
                    "file '$($sourcePaths[0].Replace("'", "''"))'",
                    "file '$($silencePath.Replace("'", "''"))'",
                    "file '$($sourcePaths[1].Replace("'", "''"))'",
                    "file '$($silencePath.Replace("'", "''"))'",
                    "file '$($sourcePaths[2].Replace("'", "''"))'"
                )
                [IO.File]::WriteAllLines($concatListPath, $lines, $utf8)
                & $ffmpeg.Source -nostdin -hide_banner -loglevel error -y -f concat -safe 0 -i $concatListPath -c:a pcm_s16le $composedRawPath
                if ($LASTEXITCODE -ne 0) { throw "Zusammensetzen fehlgeschlagen: $($triplet.id)" }
            }
            finally {
                Remove-Item -LiteralPath $concatListPath, $silencePath -Force -ErrorAction SilentlyContinue
            }
        }
        Assert-SourceFormat -Path $composedRawPath
        Convert-ToMeasurementFormat -SourcePath $completeRawPath -DestinationPath $completePath
        Convert-ToMeasurementFormat -SourcePath $composedRawPath -DestinationPath $composedPath

        $entries.Add([ordered]@{
            id = $triplet.id
            digits = @($triplet.digits)
            words = $words
            complete = [ordered]@{
                rawFile = "raw/complete/$($triplet.id).wav"; rawSha256 = Get-Sha256 $completeRawPath
                measurementFile = "normalized/complete/$($triplet.id).wav"; measurementSha256 = Get-Sha256 $completePath
            }
            composed = [ordered]@{
                rawFile = "raw/composed/$($triplet.id).wav"; rawSha256 = Get-Sha256 $composedRawPath
                measurementFile = "normalized/composed/$($triplet.id).wav"; measurementSha256 = Get-Sha256 $composedPath
            }
        })
    }

    Add-Preview -Variant 'complete' -SourcePaths @($entries | ForEach-Object { Join-Path $OutputDirectory $_.complete.measurementFile })
    Add-Preview -Variant 'composed' -SourcePaths @($entries | ForEach-Object { Join-Path $OutputDirectory $_.composed.measurementFile })

    $manifest = [ordered]@{
        schemaVersion = 1
        purpose = 'Experimental comparison of complete versus composed German digit triplets; not a clinical or validated test material.'
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        provider = 'Azure AI Speech'
        voice = $VoiceName
        modelRevision = 'service-managed-current-2026-09-08'
        locale = 'de-DE'
        ratePercent = $RatePercent
        digitVocabulary = $digitWords
        interDigitPauseMilliseconds = $InterDigitPauseMilliseconds
        rawFormat = 'PCM16 mono 24000 Hz'
        measurementFormat = 'PCM16 mono 22050 Hz, sample peak -6.0 dBFS'
        aiGeneratedVoiceDisclosure = 'Alle Audiodateien dieses Piloten enthalten die KI-generierte Azure-Stimme Ralf und keine menschliche Aufnahme.'
        entries = @($entries)
    }
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'manifest.json'), (($manifest | ConvertTo-Json -Depth 10) + [Environment]::NewLine), $utf8)
    $readme = @"
# Azure-Zifferntripel-Pilot

Dieser Pilot vergleicht 12 identische deutsche Zifferntripel in zwei Formen:

- `complete`: jedes Tripel als eine Azure-SSML-Anfrage mit festen 400-ms-Pausen;
- `composed`: dieselben drei einzeln synthetisierten Ziffern mit denselben festen Pausen zusammengesetzt.

Alle Messdateien sind PCM16, mono, 22,05 kHz und auf -6,0 dBFS Spitzenwert normalisiert. Die beiden Vorschauen enthalten die Tripel in der Manifest-Reihenfolge, jeweils mit 750 ms Abstand. Der Pilot entscheidet nur über Aussprache, Rhythmus und Wiedererkennbarkeit; er ist weder klinisch validiert noch Teil des aktiven Stimuluspakets.
"@
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'README.md'), $readme, $utf8)
    Write-Host "Zifferntripel-Pilot erzeugt: $OutputDirectory"
}
finally {
    $subscriptionKey = $null
}
