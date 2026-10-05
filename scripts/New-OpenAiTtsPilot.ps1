[CmdletBinding()]
param(
    [string]$CatalogSourcePath = (Join-Path $PSScriptRoot '..\stimuli\de-DE\personal-relative-v1\catalog-source.csv'),

    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\diagnostics\audio\openai-tts-ab-2026-09-01'),

    [string]$Model = 'gpt-4o-mini-tts-2025-12-15',

    [ValidateSet('marin', 'cedar')]
    [string[]]$Voices = @('marin', 'cedar'),

    [ValidateRange(0.25, 4.0)]
    [double]$Speed = 0.9,

    [switch]$ForceNormalization,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$apiKey = [Environment]::GetEnvironmentVariable('OPENAI_API_KEY')
if ([string]::IsNullOrWhiteSpace($apiKey)) {
    throw 'OPENAI_API_KEY ist nicht gesetzt.'
}

if (-not (Test-Path -LiteralPath $CatalogSourcePath -PathType Leaf)) {
    throw "Stimulusquellkatalog fehlt: $CatalogSourcePath"
}

$ffmpeg = Get-Command ffmpeg -ErrorAction Stop
$ffprobe = Get-Command ffprobe -ErrorAction Stop
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
$invariantCulture = [System.Globalization.CultureInfo]::InvariantCulture
$targetPeakDbfs = -6.0
$peakToleranceDb = 0.25
$targetSampleRate = 22050
$instructions = @'
Sprich Standarddeutsch, neutral und besonders deutlich. Artikuliere jeden Laut sauber und natürlich, mit gleichmäßigem, leicht reduziertem Tempo. Betone das Wort nicht dramatisch. Sprich ausschließlich den angegebenen Stimulus, ohne Einleitung, Wiederholung oder Ergänzung.
'@.Trim()

$pilotStimulusIds = @(
    'num-a-08',
    'num-b-06',
    'num-d-01',
    'num-d-13',
    'mono-a-03',
    'mono-a-08',
    'mono-b-12',
    'mono-d-05',
    'poly-a-08',
    'poly-a-18',
    'poly-a-19',
    'poly-d-19'
)

$catalogRows = @(Import-Csv -LiteralPath $CatalogSourcePath -Delimiter ';')
$rowsById = @{}
foreach ($row in $catalogRows) {
    $rowsById[$row.stimulus_id] = $row
}

$pilotRows = foreach ($stimulusId in $pilotStimulusIds) {
    if (-not $rowsById.ContainsKey($stimulusId)) {
        throw "Pilotstimulus '$stimulusId' fehlt im Katalog."
    }
    $rowsById[$stimulusId]
}

function Invoke-OpenAiSpeech {
    param(
        [Parameter(Mandatory)][string]$Voice,
        [Parameter(Mandatory)][string]$InputText,
        [Parameter(Mandatory)][string]$DestinationPath
    )

    $requestBody = [ordered]@{
        model = $Model
        voice = $Voice
        input = $InputText
        instructions = $instructions
        response_format = 'wav'
        speed = $Speed
    }
    $requestBytes = $utf8WithoutBom.GetBytes(($requestBody | ConvertTo-Json -Depth 4 -Compress))
    $headers = @{ Authorization = "Bearer $apiKey" }

    for ($attempt = 1; $attempt -le 2; $attempt++) {
        try {
            Invoke-WebRequest `
                -Uri 'https://api.openai.com/v1/audio/speech' `
                -Method Post `
                -Headers $headers `
                -ContentType 'application/json; charset=utf-8' `
                -Body $requestBytes `
                -OutFile $DestinationPath | Out-Null
            return
        }
        catch {
            if ($attempt -eq 2) {
                throw
            }
            Start-Sleep -Seconds 2
        }
    }
}

function Get-MaxVolumeDb {
    param([Parameter(Mandatory)][string]$Path)

    $analysis = & $ffmpeg.Source -nostdin -hide_banner -i $Path -af volumedetect -f null NUL 2>&1
    $match = [regex]::Match(($analysis -join "`n"), 'max_volume:\s*(-?[0-9]+(?:\.[0-9]+)?)\s*dB')
    if (-not $match.Success) {
        throw "Spitzenpegel konnte nicht bestimmt werden: $Path"
    }
    return [double]::Parse($match.Groups[1].Value, $invariantCulture)
}

function Get-AudioMetadata {
    param([Parameter(Mandatory)][string]$Path)

    $metadataJson = & $ffprobe.Source `
        -v error `
        -select_streams a:0 `
        -show_entries stream=codec_name,sample_rate,channels,bits_per_sample,duration `
        -of json `
        $Path
    if ($LASTEXITCODE -ne 0) {
        throw "ffprobe ist für '$Path' fehlgeschlagen."
    }
    $stream = (($metadataJson -join "`n") | ConvertFrom-Json).streams[0]
    return [ordered]@{
        codec = [string]$stream.codec_name
        sampleRate = [int]$stream.sample_rate
        channels = [int]$stream.channels
        bitsPerSample = [int]$stream.bits_per_sample
        durationSeconds = [math]::Round([double]::Parse([string]$stream.duration, $invariantCulture), 3)
    }
}

function Convert-ToPilotFormat {
    param(
        [Parameter(Mandatory)][string]$SourcePath,
        [Parameter(Mandatory)][string]$DestinationPath
    )

    $convertedPath = "$DestinationPath.pre-normalize.wav"
    try {
        & $ffmpeg.Source `
            -nostdin `
            -hide_banner `
            -loglevel error `
            -y `
            -i $SourcePath `
            -ar $targetSampleRate `
            -ac 1 `
            -c:a pcm_s16le `
            $convertedPath
        if ($LASTEXITCODE -ne 0) {
            throw "Audio-Konvertierung ist für '$SourcePath' fehlgeschlagen."
        }

        $convertedPeak = Get-MaxVolumeDb -Path $convertedPath
        $gainDb = $targetPeakDbfs - $convertedPeak
        $gainText = $gainDb.ToString('0.00', $invariantCulture)

        & $ffmpeg.Source `
            -nostdin `
            -hide_banner `
            -loglevel error `
            -y `
            -i $convertedPath `
            -af "volume=${gainText}dB" `
            -c:a pcm_s16le `
            $DestinationPath
        if ($LASTEXITCODE -ne 0) {
            throw "Audio-Normalisierung ist für '$SourcePath' fehlgeschlagen."
        }
    }
    finally {
        Remove-Item -LiteralPath $convertedPath -Force -ErrorAction SilentlyContinue
    }

    $normalizedPeak = Get-MaxVolumeDb -Path $DestinationPath
    # volumedetect reports one decimal place and resampling can move a sample
    # peak by another rounding step. Keep the acceptance window explicit.
    if ([math]::Abs($normalizedPeak - $targetPeakDbfs) -gt $peakToleranceDb) {
        throw "Unerwarteter Spitzenpegel für '$DestinationPath': $normalizedPeak dBFS."
    }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$entries = [System.Collections.Generic.List[object]]::new()

foreach ($voice in $Voices) {
    $rawDirectory = Join-Path $OutputDirectory "raw\$voice"
    $normalizedDirectory = Join-Path $OutputDirectory "normalized\$voice"
    New-Item -ItemType Directory -Path $rawDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $normalizedDirectory -Force | Out-Null

    foreach ($row in $pilotRows) {
        $rawPath = Join-Path $rawDirectory "$($row.stimulus_id).wav"
        $normalizedPath = Join-Path $normalizedDirectory "$($row.stimulus_id).wav"

        if ($Force -or -not (Test-Path -LiteralPath $rawPath -PathType Leaf)) {
            Invoke-OpenAiSpeech -Voice $voice -InputText $row.spoken_text -DestinationPath $rawPath
        }
        if ($Force -or $ForceNormalization -or -not (Test-Path -LiteralPath $normalizedPath -PathType Leaf)) {
            Convert-ToPilotFormat -SourcePath $rawPath -DestinationPath $normalizedPath
        }

        $rawMetadata = Get-AudioMetadata -Path $rawPath
        $normalizedMetadata = Get-AudioMetadata -Path $normalizedPath
        if ($normalizedMetadata.codec -ne 'pcm_s16le' -or
            $normalizedMetadata.sampleRate -ne $targetSampleRate -or
            $normalizedMetadata.channels -ne 1 -or
            $normalizedMetadata.bitsPerSample -ne 16) {
            throw "Pilotformat stimmt für '$normalizedPath' nicht."
        }

        $entries.Add([ordered]@{
            voice = $voice
            stimulusId = $row.stimulus_id
            material = $row.material
            spokenText = $row.spoken_text
            raw = [ordered]@{
                file = [System.IO.Path]::GetRelativePath($OutputDirectory, $rawPath).Replace('\', '/')
                sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $rawPath).Hash.ToLowerInvariant()
                metadata = $rawMetadata
            }
            normalized = [ordered]@{
                file = [System.IO.Path]::GetRelativePath($OutputDirectory, $normalizedPath).Replace('\', '/')
                sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $normalizedPath).Hash.ToLowerInvariant()
                peakDbfs = Get-MaxVolumeDb -Path $normalizedPath
                metadata = $normalizedMetadata
            }
        })
    }

    $silencePath = Join-Path $OutputDirectory '_preview-silence.wav'
    if (-not (Test-Path -LiteralPath $silencePath -PathType Leaf)) {
        & $ffmpeg.Source -nostdin -hide_banner -loglevel error -y `
            -f lavfi -i "anullsrc=r=${targetSampleRate}:cl=mono" -t 1.0 -c:a pcm_s16le $silencePath
        if ($LASTEXITCODE -ne 0) {
            throw 'Vorschausilenz konnte nicht erzeugt werden.'
        }
    }

    $concatListPath = Join-Path $OutputDirectory "_preview-$voice.txt"
    $concatLines = [System.Collections.Generic.List[string]]::new()
    foreach ($row in $pilotRows) {
        $normalizedPath = Join-Path $normalizedDirectory "$($row.stimulus_id).wav"
        $concatLines.Add("file '$($normalizedPath.Replace("'", "''"))'")
        $concatLines.Add("file '$($silencePath.Replace("'", "''"))'")
    }
    [System.IO.File]::WriteAllLines($concatListPath, $concatLines, $utf8WithoutBom)

    $previewPath = Join-Path $OutputDirectory "preview-$voice.wav"
    & $ffmpeg.Source -nostdin -hide_banner -loglevel error -y `
        -f concat -safe 0 -i $concatListPath -c:a pcm_s16le $previewPath
    if ($LASTEXITCODE -ne 0) {
        throw "Vorschau konnte für '$voice' nicht erzeugt werden."
    }
}

foreach ($temporaryPath in @(
    (Join-Path $OutputDirectory '_preview-silence.wav'),
    (Join-Path $OutputDirectory '_preview-marin.txt'),
    (Join-Path $OutputDirectory '_preview-cedar.txt')
)) {
    if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}

$ffmpegVersion = (& $ffmpeg.Source -version | Select-Object -First 1)
$manifest = [ordered]@{
    schemaVersion = 1
    purpose = 'Review-only A/B pilot for a clearer German synthetic stimulus voice'
    notForMeasurement = $true
    aiGeneratedVoiceDisclosure = 'Alle Audiodateien enthalten KI-generierte, nicht menschlich eingesprochene Stimmen.'
    generatedAt = [DateTimeOffset]::Now.ToString('o')
    sourceCatalog = [System.IO.Path]::GetRelativePath($OutputDirectory, $CatalogSourcePath).Replace('\', '/')
    model = $Model
    voices = @($Voices)
    instructions = $instructions
    speed = $Speed
    responseFormat = 'wav'
    normalization = [ordered]@{
        encoding = 'PCM_SIGNED'
        sampleRate = $targetSampleRate
        bitsPerSample = 16
        channels = 1
        targetSamplePeakDbfs = $targetPeakDbfs
        silenceTrimmed = $false
        ffmpeg = $ffmpegVersion
    }
    stimulusOrder = @($pilotRows | ForEach-Object { $_.stimulus_id })
    entries = @($entries)
    previews = @($Voices | ForEach-Object {
        $previewPath = Join-Path $OutputDirectory "preview-$_.wav"
        [ordered]@{
            voice = $_
            file = [System.IO.Path]::GetRelativePath($OutputDirectory, $previewPath).Replace('\', '/')
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $previewPath).Hash.ToLowerInvariant()
            metadata = Get-AudioMetadata -Path $previewPath
        }
    })
}

$manifestPath = Join-Path $OutputDirectory 'manifest.json'
[System.IO.File]::WriteAllText(
    $manifestPath,
    (($manifest | ConvertTo-Json -Depth 10) + [Environment]::NewLine),
    $utf8WithoutBom)

Write-Host "OpenAI-TTS-Pilot erzeugt: $($entries.Count) Einzeldateien und $($Voices.Count) Vorschauen."
Write-Host "Ausgabe: $OutputDirectory"
Write-Host "Manifest: $manifestPath"
