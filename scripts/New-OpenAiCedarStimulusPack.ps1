[CmdletBinding()]
param(
    [string]$ContrastSourcePath = (Join-Path $PSScriptRoot '..\stimuli\de-DE\personal-phoneme-contrast-v1\contrast-groups.csv'),

    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\stimuli\de-DE\personal-phoneme-contrast-v1'),

    [string]$Model = 'gpt-4o-mini-tts-2025-12-15',

    [ValidateSet('cedar')]
    [string]$Voice = 'cedar',

    [ValidateRange(0.25, 4.0)]
    [double]$Speed = 0.9,

    [switch]$ForceApi,

    [switch]$ForceNormalization,

    [switch]$CatalogOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ContrastSourcePath -PathType Leaf)) {
    throw "Kontrastgruppenquelle fehlt: $ContrastSourcePath"
}

$ffmpeg = Get-Command ffmpeg -ErrorAction Stop
$ffprobe = Get-Command ffprobe -ErrorAction Stop
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
$invariantCulture = [System.Globalization.CultureInfo]::InvariantCulture
$targetPeakDbfs = -6.0
$peakToleranceDb = 0.25
$targetSampleRate = 22050
$catalogId = 'de-DE-personal-phoneme-contrast-v1'
$catalogVersion = '1.0.0'
$instructions = @'
Sprich Standarddeutsch, neutral und besonders deutlich. Artikuliere jeden Laut sauber und natürlich, mit gleichmäßigem, leicht reduziertem Tempo. Betone das Wort nicht dramatisch. Sprich ausschließlich den angegebenen Stimulus, ohne Einleitung, Wiederholung oder Ergänzung.
'@.Trim()
$aiDisclosure = 'Alle Audiodateien dieses Pakets enthalten die KI-generierte OpenAI-Stimme cedar und keine menschliche Aufnahme.'

function Get-Sha256Text {
    param([Parameter(Mandatory)][string]$Text)

    return ConvertTo-HexString ([System.Security.Cryptography.SHA256]::HashData($utf8WithoutBom.GetBytes($Text)))
}

function ConvertTo-HexString {
    param([Parameter(Mandatory)][byte[]]$Bytes)

    return [Convert]::ToHexString($Bytes).ToLowerInvariant()
}

function Invoke-OpenAiSpeech {
    param(
        [Parameter(Mandatory)][string]$InputText,
        [Parameter(Mandatory)][string]$DestinationPath
    )

    $apiKey = [Environment]::GetEnvironmentVariable('OPENAI_API_KEY')
    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        throw 'OPENAI_API_KEY ist für fehlende Rohdateien oder -ForceApi nicht gesetzt.'
    }

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
    $temporaryPath = "$DestinationPath.download"

    try {
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            try {
                Invoke-WebRequest `
                    -Uri 'https://api.openai.com/v1/audio/speech' `
                    -Method Post `
                    -Headers $headers `
                    -ContentType 'application/json; charset=utf-8' `
                    -Body $requestBytes `
                    -OutFile $temporaryPath | Out-Null
                Move-Item -LiteralPath $temporaryPath -Destination $DestinationPath -Force
                return
            }
            catch {
                Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
                if ($attempt -eq 3) {
                    throw
                }
                Start-Sleep -Seconds ([math]::Pow(2, $attempt))
            }
        }
    }
    finally {
        Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
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

function Convert-ToMeasurementFormat {
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
    if ([math]::Abs($normalizedPeak - $targetPeakDbfs) -gt $peakToleranceDb) {
        throw "Unerwarteter Spitzenpegel für '$DestinationPath': $normalizedPeak dBFS."
    }
}

$groups = @(Import-Csv -LiteralPath $ContrastSourcePath -Delimiter ';')
if ($groups.Count -ne 25) {
    throw "Es werden genau 25 Kontrastgruppen erwartet, gefunden: $($groups.Count)."
}
if (($groups | Group-Object group_id | Where-Object Count -gt 1)) {
    throw 'Kontrastgruppen-IDs müssen eindeutig sein.'
}

$positionCounts = @{}
foreach ($position in @('initial', 'vowel', 'final')) {
    $positionCounts[$position] = @($groups | Where-Object contrast_position -eq $position).Count
}
if ($positionCounts.initial -ne 8 -or $positionCounts.vowel -ne 9 -or $positionCounts.final -ne 8) {
    throw "Kontrastverteilung muss 8/9/8 sein, gefunden: $($positionCounts.initial)/$($positionCounts.vowel)/$($positionCounts.final)."
}

$allWords = [System.Collections.Generic.List[string]]::new()
$preparedGroups = [System.Collections.Generic.List[object]]::new()
foreach ($group in $groups) {
    if ($group.contrast_position -notin @('initial', 'vowel', 'final')) {
        throw "Ungültige Kontrastposition in '$($group.group_id)'."
    }
    if ($group.cue_category -notin @('broad', 'low-mid', 'mid', 'high')) {
        throw "Ungültige Cue-Kategorie in '$($group.group_id)'."
    }

    $alternatives = [System.Collections.Generic.List[object]]::new()
    foreach ($index in 1..5) {
        $text = [string]$group."alternative_$index"
        $ipa = [string]$group."ipa_$index"
        $phoneme = [string]$group."phoneme_$index"
        if ([string]::IsNullOrWhiteSpace($text) -or
            [string]::IsNullOrWhiteSpace($ipa) -or
            [string]::IsNullOrWhiteSpace($phoneme) -or
            -not ($ipa.StartsWith('/') -and $ipa.EndsWith('/'))) {
            throw "Alternative $index in '$($group.group_id)' ist unvollständig."
        }
        $allWords.Add($text)
        $alternatives.Add([ordered]@{
            index = $index
            text = $text
            ipa = $ipa
            phoneme = $phoneme
        })
    }
    if (@($alternatives.text | Sort-Object -Unique).Count -ne 5 -or
        @($alternatives.ipa | Sort-Object -Unique).Count -ne 5) {
        throw "Kontrastgruppe '$($group.group_id)' enthält nicht fünf eindeutige Alternativen."
    }
    $preparedGroups.Add([ordered]@{
        id = $group.group_id
        position = $group.contrast_position
        cueCategory = $group.cue_category
        alternatives = @($alternatives)
    })
}

$duplicateWords = $allWords | Group-Object { $_.ToLowerInvariant() } | Where-Object Count -gt 1
if ($duplicateWords) {
    throw "Gruppenübergreifend doppelte Wörter: $($duplicateWords.Group[0] -join ', ')."
}

$catalogLists = [System.Collections.Generic.List[object]]::new()
$audioTargets = [System.Collections.Generic.List[object]]::new()
foreach ($listOffset in 0..4) {
    $listLetter = [char]([int][char]'a' + $listOffset)
    $items = [System.Collections.Generic.List[object]]::new()
    for ($groupOffset = 0; $groupOffset -lt $preparedGroups.Count; $groupOffset++) {
        $group = $preparedGroups[$groupOffset]
        $targetIndex = (($groupOffset + $listOffset) % 5) + 1
        $target = $group.alternatives[$targetIndex - 1]
        $stimulusId = "$($group.id)-$targetIndex"
        $item = [ordered]@{
            id = $stimulusId
            spokenText = $target.text
            canonicalResponse = $target.text
            audioFile = "audio/$stimulusId.wav"
            contrastGroupId = $group.id
            contrastPosition = $group.position
            targetIpa = $target.ipa
            targetPhoneme = $target.phoneme
            cueCategory = $group.cueCategory
            targetAlternativeIndex = $targetIndex
            responseAlternatives = @($group.alternatives)
        }
        $items.Add($item)
        $audioTargets.Add($item)
    }
    $catalogLists.Add([ordered]@{
        id = "contrast-$listLetter"
        material = 'phonemeContrasts'
        items = @($items)
    })
}

if (@($audioTargets.id | Sort-Object -Unique).Count -ne 125) {
    throw 'Die Zielrotation erzeugt nicht 125 eindeutige Stimulus-IDs.'
}
foreach ($list in $catalogLists) {
    $targetCounts = @($list.items | ForEach-Object { $_.targetAlternativeIndex } | Group-Object)
    if ($targetCounts.Count -ne 5 -or ($targetCounts | Where-Object Count -ne 5)) {
        throw "Liste '$($list.id)' balanciert die fünf Zielpositionen nicht gleichmäßig."
    }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$sourceAudioDirectory = Join-Path $OutputDirectory 'source-audio'
$audioDirectory = Join-Path $OutputDirectory 'audio'
New-Item -ItemType Directory -Path $sourceAudioDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $audioDirectory -Force | Out-Null

$catalog = [ordered]@{
    schemaVersion = 2
    id = $catalogId
    version = $catalogVersion
    language = 'de-DE'
    title = 'Deutsches persönliches Phonemkontrastmaterial v1'
    license = 'CC0-1.0'
    clinicallyValidated = $false
    itemsPerList = 25
    lists = @($catalogLists)
    paradigm = 'closed-set-phoneme-contrast'
    responseAlternativeCount = 5
    chanceLevelPercent = 20
}
$catalogPath = Join-Path $OutputDirectory 'catalog.json'
[System.IO.File]::WriteAllText(
    $catalogPath,
    (($catalog | ConvertTo-Json -Depth 12) + [Environment]::NewLine),
    $utf8WithoutBom)

if ($CatalogOnly) {
    Write-Host "Phonemkontrastkatalog erzeugt: $($audioTargets.Count) rotierte Ziele in $($catalogLists.Count) Listen."
    Write-Host "Katalog: $catalogPath"
    return
}

$existingIndexPath = Join-Path $OutputDirectory 'audio-index.json'
$generatedAt = if (Test-Path -LiteralPath $existingIndexPath -PathType Leaf) {
    $existingIndexJson = Get-Content -LiteralPath $existingIndexPath -Raw
    $generatedAtMatch = [regex]::Match($existingIndexJson, '"generatedAt"\s*:\s*"([^"]+)"')
    if ($generatedAtMatch.Success) {
        $generatedAtMatch.Groups[1].Value
    }
    else {
        [DateTimeOffset]::Now.ToString('o')
    }
}
else {
    [DateTimeOffset]::Now.ToString('o')
}
if ([string]::IsNullOrWhiteSpace($generatedAt) -or $ForceApi) {
    $generatedAt = [DateTimeOffset]::Now.ToString('o')
}

$entries = [System.Collections.Generic.List[object]]::new()
for ($targetOffset = 0; $targetOffset -lt $audioTargets.Count; $targetOffset++) {
    $target = $audioTargets[$targetOffset]
    $rawPath = Join-Path $sourceAudioDirectory "$($target.id).wav"
    $normalizedPath = Join-Path $audioDirectory "$($target.id).wav"

    if ($ForceApi -or -not (Test-Path -LiteralPath $rawPath -PathType Leaf)) {
        Write-Host ("[{0}/125] OpenAI cedar: {1} ({2})" -f ($targetOffset + 1), $target.spokenText, $target.id)
        Invoke-OpenAiSpeech -InputText $target.spokenText -DestinationPath $rawPath
    }
    if ($ForceApi -or $ForceNormalization -or -not (Test-Path -LiteralPath $normalizedPath -PathType Leaf)) {
        Convert-ToMeasurementFormat -SourcePath $rawPath -DestinationPath $normalizedPath
    }

    $rawMetadata = Get-AudioMetadata -Path $rawPath
    if ($rawMetadata.codec -ne 'pcm_s16le' -or
        $rawMetadata.sampleRate -ne 24000 -or
        $rawMetadata.channels -ne 1 -or
        $rawMetadata.bitsPerSample -ne 16) {
        throw "Unerwartetes OpenAI-Rohformat für '$rawPath'."
    }
    $normalizedMetadata = Get-AudioMetadata -Path $normalizedPath
    if ($normalizedMetadata.codec -ne 'pcm_s16le' -or
        $normalizedMetadata.sampleRate -ne $targetSampleRate -or
        $normalizedMetadata.channels -ne 1 -or
        $normalizedMetadata.bitsPerSample -ne 16) {
        throw "Unerwartetes Zielformat für '$normalizedPath'."
    }

    $entries.Add([ordered]@{
        stimulusId = $target.id
        audioFile = "audio/$($target.id).wav"
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $normalizedPath).Hash.ToLowerInvariant()
        byteLength = (Get-Item -LiteralPath $normalizedPath).Length
        sourceAudioFile = "source-audio/$($target.id).wav"
        sourceSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $rawPath).Hash.ToLowerInvariant()
        sourceByteLength = (Get-Item -LiteralPath $rawPath).Length
        sourceMetadata = $rawMetadata
        normalizedMetadata = $normalizedMetadata
        peakDbfs = Get-MaxVolumeDb -Path $normalizedPath
    })
}

$ffmpegVersion = (& $ffmpeg.Source -version | Select-Object -First 1)
$generationConfig = [ordered]@{
    model = $Model
    voice = $Voice
    instructions = $instructions
    speed = $Speed
    responseFormat = 'wav'
    normalization = [ordered]@{
        ffmpeg = $ffmpegVersion
        targetEncoding = 'PCM_SIGNED'
        targetSampleRate = $targetSampleRate
        targetBitsPerSample = 16
        targetChannels = 1
        targetSamplePeakDbfs = $targetPeakDbfs
        silenceTrimmed = $false
        passes = 2
    }
}
$generationConfigJson = $generationConfig | ConvertTo-Json -Depth 8 -Compress
$audioIndex = [ordered]@{
    schemaVersion = 2
    catalogId = $catalogId
    catalogVersion = $catalogVersion
    catalogSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $catalogPath).Hash.ToLowerInvariant()
    sourceGroupsFile = 'contrast-groups.csv'
    sourceGroupsSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $ContrastSourcePath).Hash.ToLowerInvariant()
    audioFormat = [ordered]@{
        encoding = 'PCM_SIGNED'
        sampleRate = $targetSampleRate
        bitsPerSample = 16
        channels = 1
    }
    generator = [ordered]@{
        name = 'OpenAI Audio API'
        version = 'v1/audio/speech'
        executableSha256 = 'not-applicable-remote-api'
        modelRepository = 'OpenAI'
        modelRevision = $Model
        modelName = $Model
        modelSha256 = 'not-published-by-provider'
        configSha256 = Get-Sha256Text -Text $generationConfigJson
        modelLicense = 'OpenAI API terms'
        datasetLicense = 'not disclosed by provider'
        syntheticVoice = $true
        noiseScale = 0
        phonemeWidthNoise = 0
        lengthScale = $Speed
        sentenceSilenceSeconds = 0
        voice = $Voice
        aiGeneratedVoice = $true
        aiGeneratedVoiceDisclosure = $aiDisclosure
        instructions = $instructions
        speed = $Speed
        responseFormat = 'wav'
        sourceAudioFormat = 'PCM_SIGNED, 24000 Hz, 16 bit, mono'
        normalization = "Two-pass FFmpeg resampling and sample-peak normalization to PCM16 mono 22050 Hz at -6.0 dBFS; $ffmpegVersion"
        generatedAt = $generatedAt
    }
    entries = @($entries)
}

[System.IO.File]::WriteAllText(
    $existingIndexPath,
    (($audioIndex | ConvertTo-Json -Depth 12) + [Environment]::NewLine),
    $utf8WithoutBom)

Write-Host "Cedar-Phonemkontrastpaket erzeugt: $($entries.Count) Roh- und $($entries.Count) Mess-WAVs."
Write-Host "Katalog: $catalogPath"
Write-Host "Audioindex: $existingIndexPath"
