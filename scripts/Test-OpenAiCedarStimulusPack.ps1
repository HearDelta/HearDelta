[CmdletBinding()]
param(
    [string]$PackageDirectory = (Join-Path $PSScriptRoot '..\stimuli\de-DE\personal-phoneme-contrast-v1'),

    [string]$ExpectedGeneratorName = 'OpenAI Audio API',

    [string]$ExpectedModelRevision = 'gpt-4o-mini-tts-2025-12-15',

    [string]$ExpectedVoice = 'cedar'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ffmpeg = Get-Command ffmpeg -ErrorAction Stop
$ffprobe = Get-Command ffprobe -ErrorAction Stop
$invariantCulture = [System.Globalization.CultureInfo]::InvariantCulture
$errors = [System.Collections.Generic.List[string]]::new()

function Add-CheckError {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not $Condition) {
        $errors.Add($Message)
    }
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
    return [pscustomobject]@{
        Codec = [string]$stream.codec_name
        SampleRate = [int]$stream.sample_rate
        Channels = [int]$stream.channels
        BitsPerSample = [int]$stream.bits_per_sample
        DurationSeconds = [double]::Parse([string]$stream.duration, $invariantCulture)
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

function Get-MaxSpectralCentroid {
    param([Parameter(Mandatory)][string]$Path)

    $analysis = & $ffmpeg.Source `
        -nostdin `
        -hide_banner `
        -loglevel error `
        -i $Path `
        -af 'silenceremove=start_periods=1:start_threshold=-50dB:stop_periods=-1:stop_threshold=-50dB,aspectralstats=measure=centroid,ametadata=print:key=lavfi.aspectralstats.1.centroid:file=-' `
        -f null `
        NUL 2>&1
    $values = @($analysis | ForEach-Object {
        $match = [regex]::Match([string]$_, 'centroid=([0-9]+(?:\.[0-9]+)?)')
        if ($match.Success) {
            [double]::Parse($match.Groups[1].Value, $invariantCulture)
        }
    })
    if ($values.Count -eq 0) {
        throw "Spektralzentrum konnte nicht bestimmt werden: $Path"
    }
    return ($values | Measure-Object -Maximum).Maximum
}

$catalogPath = Join-Path $PackageDirectory 'catalog.json'
$audioIndexPath = Join-Path $PackageDirectory 'audio-index.json'
$sourceGroupsPath = Join-Path $PackageDirectory 'contrast-groups.csv'
foreach ($requiredPath in @($catalogPath, $audioIndexPath, $sourceGroupsPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Erforderliche Paketdatei fehlt: $requiredPath"
    }
}

$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
$audioIndex = Get-Content -LiteralPath $audioIndexPath -Raw | ConvertFrom-Json
$catalogItems = @($catalog.lists.items)
$entries = @($audioIndex.entries)

Add-CheckError ($catalog.schemaVersion -eq 2) 'Der Katalog verwendet nicht Schema v2.'
Add-CheckError ($audioIndex.schemaVersion -eq 2) 'Der Audioindex verwendet nicht Schema v2.'
Add-CheckError ($catalog.id -eq 'de-DE-personal-phoneme-contrast-v1') 'Die Katalog-ID ist unerwartet.'
Add-CheckError ($catalog.lists.Count -eq 5) 'Der Katalog enthält nicht fünf Listen.'
Add-CheckError ($catalogItems.Count -eq 125) 'Der Katalog enthält nicht 125 Zielstimuli.'
Add-CheckError ($entries.Count -eq 125) 'Der Audioindex enthält nicht 125 Einträge.'
Add-CheckError ($audioIndex.generator.name -eq $ExpectedGeneratorName) "Der Audioindex kennzeichnet nicht '$ExpectedGeneratorName' als Generator."
Add-CheckError ($audioIndex.generator.modelRevision -eq $ExpectedModelRevision) "Der Audioindex enthält nicht die erwartete Modellrevision '$ExpectedModelRevision'."
Add-CheckError ($audioIndex.generator.voice -eq $ExpectedVoice) "Der Audioindex kennzeichnet nicht '$ExpectedVoice' als Stimme."
Add-CheckError ([bool]$audioIndex.generator.aiGeneratedVoice) 'Die KI-Stimmkennzeichnung fehlt.'
Add-CheckError (-not [string]::IsNullOrWhiteSpace([string]$audioIndex.generator.aiGeneratedVoiceDisclosure)) 'Der sichtbare KI-Stimmhinweis fehlt.'
Add-CheckError (
    ((Get-FileHash -Algorithm SHA256 -LiteralPath $catalogPath).Hash.ToLowerInvariant() -eq $audioIndex.catalogSha256)) `
    'Die Katalog-SHA-256 stimmt nicht mit dem Audioindex überein.'
Add-CheckError (
    ((Get-FileHash -Algorithm SHA256 -LiteralPath $sourceGroupsPath).Hash.ToLowerInvariant() -eq $audioIndex.sourceGroupsSha256)) `
    'Die SHA-256 der Kontrastgruppenquelle stimmt nicht.'

$catalogIds = @($catalogItems.id | Sort-Object)
$entryIds = @($entries.stimulusId | Sort-Object)
Add-CheckError (($catalogIds -join "`n") -eq ($entryIds -join "`n")) 'Katalog und Audioindex enthalten unterschiedliche Stimulus-IDs.'

foreach ($list in $catalog.lists) {
    $targetDistribution = @($list.items.targetAlternativeIndex | Group-Object | Sort-Object Name)
    Add-CheckError (
        ($targetDistribution.Count -eq 5 -and -not ($targetDistribution | Where-Object Count -ne 5))) `
        "Liste '$($list.id)' balanciert die fünf Zielpositionen nicht."
}
foreach ($group in ($catalogItems | Group-Object contrastGroupId)) {
    $rotation = @($group.Group.targetAlternativeIndex | Sort-Object)
    Add-CheckError (($rotation -join ',') -eq '1,2,3,4,5') "Gruppe '$($group.Name)' hat keine vollständige Zielrotation."
}

$audioFiles = @(Get-ChildItem -LiteralPath (Join-Path $PackageDirectory 'audio') -Filter '*.wav' -File)
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $PackageDirectory 'source-audio') -Filter '*.wav' -File)
Add-CheckError ($audioFiles.Count -eq 125) 'Das Messaudioverzeichnis enthält nicht genau 125 WAV-Dateien.'
Add-CheckError ($sourceFiles.Count -eq 125) 'Das Rohaudioverzeichnis enthält nicht genau 125 WAV-Dateien.'

$peaks = [System.Collections.Generic.List[double]]::new()
$durations = [System.Collections.Generic.List[double]]::new()
foreach ($entry in $entries) {
    $audioPath = Join-Path $PackageDirectory ([string]$entry.audioFile).Replace('/', '\')
    $sourcePath = Join-Path $PackageDirectory ([string]$entry.sourceAudioFile).Replace('/', '\')
    if (-not (Test-Path -LiteralPath $audioPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        $errors.Add("Audio fehlt für '$($entry.stimulusId)'.")
        continue
    }

    $audioInfo = Get-Item -LiteralPath $audioPath
    $sourceInfo = Get-Item -LiteralPath $sourcePath
    Add-CheckError ($audioInfo.Length -eq $entry.byteLength) "Messdateilänge stimmt für '$($entry.stimulusId)' nicht."
    Add-CheckError ($sourceInfo.Length -eq $entry.sourceByteLength) "Rohdateilänge stimmt für '$($entry.stimulusId)' nicht."
    Add-CheckError (
        ((Get-FileHash -Algorithm SHA256 -LiteralPath $audioPath).Hash.ToLowerInvariant() -eq $entry.sha256)) `
        "Messdatei-SHA-256 stimmt für '$($entry.stimulusId)' nicht."
    Add-CheckError (
        ((Get-FileHash -Algorithm SHA256 -LiteralPath $sourcePath).Hash.ToLowerInvariant() -eq $entry.sourceSha256)) `
        "Rohdatei-SHA-256 stimmt für '$($entry.stimulusId)' nicht."

    $audioMetadata = Get-AudioMetadata -Path $audioPath
    $sourceMetadata = Get-AudioMetadata -Path $sourcePath
    Add-CheckError (
        ($audioMetadata.Codec -eq 'pcm_s16le' -and $audioMetadata.SampleRate -eq 22050 -and
         $audioMetadata.Channels -eq 1 -and $audioMetadata.BitsPerSample -eq 16)) `
        "Messformat stimmt für '$($entry.stimulusId)' nicht."
    Add-CheckError (
        ($sourceMetadata.Codec -eq 'pcm_s16le' -and $sourceMetadata.SampleRate -eq 24000 -and
         $sourceMetadata.Channels -eq 1 -and $sourceMetadata.BitsPerSample -eq 16)) `
        "Rohformat stimmt für '$($entry.stimulusId)' nicht."

    $peak = Get-MaxVolumeDb -Path $audioPath
    $peaks.Add($peak)
    $durations.Add($audioMetadata.DurationSeconds)
    Add-CheckError ([math]::Abs($peak - -6.0) -le 0.25) "Spitzenpegel stimmt für '$($entry.stimulusId)' nicht: $peak dBFS."
}

$sCentroid = Get-MaxSpectralCentroid -Path (Join-Path $PackageDirectory 'audio\initial-05-1.wav')
$shCentroid = Get-MaxSpectralCentroid -Path (Join-Path $PackageDirectory 'audio\initial-05-2.wav')
Add-CheckError (
    ($sCentroid -ge ($shCentroid + 500.0))) `
    "Der erwartete spektrale Abstand zwischen /s/ in 'Sicht' und /sch/ in 'Schicht' ist zu klein."

if ($errors.Count -gt 0) {
    throw "Stimuluspaketprüfung fehlgeschlagen:`n- $($errors -join "`n- ")"
}

[pscustomobject]@{
    CatalogId = $catalog.id
    Lists = $catalog.lists.Count
    ContrastGroups = @($catalogItems.contrastGroupId | Sort-Object -Unique).Count
    Stimuli = $catalogItems.Count
    SourceWavs = $sourceFiles.Count
    MeasurementWavs = $audioFiles.Count
    MinimumPeakDbfs = ($peaks | Measure-Object -Minimum).Minimum
    MaximumPeakDbfs = ($peaks | Measure-Object -Maximum).Maximum
    MinimumDurationSeconds = [math]::Round(($durations | Measure-Object -Minimum).Minimum, 3)
    MaximumDurationSeconds = [math]::Round(($durations | Measure-Object -Maximum).Maximum, 3)
    SichtMaxCentroidHz = [math]::Round($sCentroid, 1)
    SchichtMaxCentroidHz = [math]::Round($shCentroid, 1)
    Errors = 0
} | Format-List
