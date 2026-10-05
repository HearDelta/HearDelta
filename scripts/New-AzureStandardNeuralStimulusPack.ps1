[CmdletBinding()]
param(
    [string]$ContrastSourcePath = (Join-Path $PSScriptRoot '..\stimuli\de-DE\personal-phoneme-contrast-v1\contrast-groups.csv'),

    [string]$CatalogGeneratorPath = (Join-Path $PSScriptRoot 'New-OpenAiCedarStimulusPack.ps1'),

    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\diagnostics\audio\azure-standard-neural-ralf-full-2026-09-03'),

    [string]$VoiceName = 'de-DE-RalfNeural',

    [ValidatePattern('^[a-z0-9-]+$')]
    [string]$VoiceSlug = 'ralf',

    [string]$VoiceDisplayName = 'Ralf',

    [ValidateRange(-50, 100)]
    [int]$RatePercent = -30,

    [switch]$ForceApi,

    [switch]$ForceNormalization
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

foreach ($requiredPath in @($ContrastSourcePath, $CatalogGeneratorPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Erforderliche Quelldatei fehlt: $requiredPath"
    }
}

$subscriptionKey = [Environment]::GetEnvironmentVariable('AZURE_SPEECH_KEY')
if ([string]::IsNullOrWhiteSpace($subscriptionKey)) {
    throw 'AZURE_SPEECH_KEY ist nicht gesetzt.'
}
$speechRegion = [Environment]::GetEnvironmentVariable('AZURE_SPEECH_REGION')
if ([string]::IsNullOrWhiteSpace($speechRegion) -or $speechRegion -notmatch '^[a-z0-9]+$') {
    throw 'AZURE_SPEECH_REGION fehlt oder hat ein unerwartetes Format.'
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$ffmpeg = Get-Command ffmpeg -ErrorAction Stop
$ffprobe = Get-Command ffprobe -ErrorAction Stop
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
$invariantCulture = [System.Globalization.CultureInfo]::InvariantCulture
$targetPeakDbfs = -6.0
$peakToleranceDb = 0.25
$targetSampleRate = 22050
$azureOutputFormat = 'riff-24khz-16bit-mono-pcm'
$speechEndpoint = "https://$speechRegion.tts.speech.microsoft.com/cognitiveservices/v1"
$modelRevision = 'service-managed-current-2026-09-03'
$aiDisclosure = "Alle Audiodateien dieses Pakets enthalten die KI-generierte Azure-Stimme $VoiceDisplayName und keine menschliche Aufnahme."
$rateDescription = if ($RatePercent -eq 0) { 'Standardtempo' } else { "SSML-Tempo $RatePercent Prozent" }
$ssmlDescription = "Isoliertes deutsches Zielwort mit kataloggebundener IPA-Aussprache und $rateDescription; kein Trägersatz und kein nachträglicher Schnitt."

function ConvertTo-HexString {
    param([Parameter(Mandatory)][byte[]]$Bytes)

    return [Convert]::ToHexString($Bytes).ToLowerInvariant()
}

function Get-Sha256Text {
    param([Parameter(Mandatory)][string]$Text)

    return ConvertTo-HexString ([System.Security.Cryptography.SHA256]::HashData($utf8WithoutBom.GetBytes($Text)))
}

function Invoke-AzureSpeech {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Ipa,
        [Parameter(Mandatory)][string]$DestinationPath
    )

    $escapedText = [System.Security.SecurityElement]::Escape($Text)
    $escapedIpa = [System.Security.SecurityElement]::Escape($Ipa)
    $rateValue = if ($RatePercent -gt 0) { "+$RatePercent%" } else { "$RatePercent%" }
    $speechBody = "<lang xml:lang=`"de-DE`"><phoneme alphabet=`"ipa`" ph=`"$escapedIpa`">$escapedText</phoneme></lang>"
    if ($RatePercent -ne 0) {
        $speechBody = "<prosody rate=`"$rateValue`">$speechBody</prosody>"
    }
    $ssml = @"
<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="de-DE">
  <voice name="$VoiceName">
    $speechBody
  </voice>
</speak>
"@
    $requestBytes = $utf8WithoutBom.GetBytes($ssml)
    $headers = @{
        'Ocp-Apim-Subscription-Key' = $subscriptionKey
        'X-Microsoft-OutputFormat' = $azureOutputFormat
        'User-Agent' = 'HearDelta-AzureStandardNeuralStimulusPack'
    }
    $temporaryPath = "$DestinationPath.download"

    try {
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            try {
                Invoke-WebRequest `
                    -Uri $speechEndpoint `
                    -Method Post `
                    -Headers $headers `
                    -ContentType 'application/ssml+xml; charset=utf-8' `
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

function Get-ActiveSignalSeconds {
    param([Parameter(Mandatory)][string]$Path)

    $durationText = & $ffprobe.Source `
        -v error `
        -show_entries format=duration `
        -of default=noprint_wrappers=1:nokey=1 `
        $Path
    if ($LASTEXITCODE -ne 0) {
        throw "Audiodauer konnte für '$Path' nicht bestimmt werden."
    }
    $duration = [double]::Parse([string]($durationText | Select-Object -First 1), $invariantCulture)
    $analysis = & $ffmpeg.Source `
        -nostdin `
        -hide_banner `
        -i $Path `
        -af 'silencedetect=noise=-40dB:d=0.05' `
        -f null `
        NUL 2>&1
    $silenceDuration = 0.0
    foreach ($match in [regex]::Matches(($analysis -join "`n"), 'silence_duration:\s*([0-9]+(?:\.[0-9]+)?)')) {
        $silenceDuration += [double]::Parse($match.Groups[1].Value, $invariantCulture)
    }
    return [math]::Round([math]::Max(0.0, [double]($duration - $silenceDuration)), 3)
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

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
& $CatalogGeneratorPath `
    -ContrastSourcePath $ContrastSourcePath `
    -OutputDirectory $OutputDirectory `
    -CatalogOnly
Copy-Item -LiteralPath $ContrastSourcePath -Destination (Join-Path $OutputDirectory 'contrast-groups.csv') -Force

$catalogPath = Join-Path $OutputDirectory 'catalog.json'
$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
$audioTargets = @($catalog.lists.items)
if ($audioTargets.Count -ne 125 -or @($audioTargets.id | Sort-Object -Unique).Count -ne 125) {
    throw 'Der erzeugte Katalog enthält nicht 125 eindeutige Stimuli.'
}
foreach ($target in $audioTargets) {
    if ([string]::IsNullOrWhiteSpace([string]$target.spokenText) -or
        [string]::IsNullOrWhiteSpace([string]$target.targetIpa) -or
        -not ([string]$target.targetIpa).StartsWith('/') -or
        -not ([string]$target.targetIpa).EndsWith('/')) {
        throw "Stimulus '$($target.id)' enthält keine gültige kataloggebundene IPA-Notation."
    }
}

$sourceAudioDirectory = Join-Path $OutputDirectory 'source-audio'
$audioDirectory = Join-Path $OutputDirectory 'audio'
New-Item -ItemType Directory -Path $sourceAudioDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $audioDirectory -Force | Out-Null

$existingIndexPath = Join-Path $OutputDirectory 'audio-index.json'
$generatedAt = if (Test-Path -LiteralPath $existingIndexPath -PathType Leaf) {
    $existingIndex = Get-Content -LiteralPath $existingIndexPath -Raw | ConvertFrom-Json
    [string]$existingIndex.generator.generatedAt
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
    $ipa = ([string]$target.targetIpa).Trim('/')

    if ($ForceApi -or -not (Test-Path -LiteralPath $rawPath -PathType Leaf)) {
        Write-Host ("[{0}/125] Azure {1} IPA: {2} ({3})" -f ($targetOffset + 1), $VoiceDisplayName, $target.spokenText, $target.id)
        Invoke-AzureSpeech -Text ([string]$target.spokenText) -Ipa $ipa -DestinationPath $rawPath
    }
    if ($ForceApi -or $ForceNormalization -or -not (Test-Path -LiteralPath $normalizedPath -PathType Leaf)) {
        Convert-ToMeasurementFormat -SourcePath $rawPath -DestinationPath $normalizedPath
    }

    $rawMetadata = Get-AudioMetadata -Path $rawPath
    $normalizedMetadata = Get-AudioMetadata -Path $normalizedPath
    if ($rawMetadata.codec -ne 'pcm_s16le' -or $rawMetadata.sampleRate -ne 24000 -or
        $rawMetadata.channels -ne 1 -or $rawMetadata.bitsPerSample -ne 16) {
        throw "Unerwartetes Azure-Rohformat für '$rawPath'."
    }
    if ($normalizedMetadata.codec -ne 'pcm_s16le' -or $normalizedMetadata.sampleRate -ne $targetSampleRate -or
        $normalizedMetadata.channels -ne 1 -or $normalizedMetadata.bitsPerSample -ne 16) {
        throw "Unerwartetes Zielformat für '$normalizedPath'."
    }

    $activeSignalSeconds = Get-ActiveSignalSeconds -Path $normalizedPath
    if ($activeSignalSeconds -lt 0.12) {
        throw "Zu wenig anhaltendes Nutzsignal für '$($target.spokenText)' ($activeSignalSeconds s)."
    }
    $entries.Add([ordered]@{
        stimulusId = [string]$target.id
        audioFile = "audio/$($target.id).wav"
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $normalizedPath).Hash.ToLowerInvariant()
        byteLength = (Get-Item -LiteralPath $normalizedPath).Length
        sourceAudioFile = "source-audio/$($target.id).wav"
        sourceSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $rawPath).Hash.ToLowerInvariant()
        sourceByteLength = (Get-Item -LiteralPath $rawPath).Length
        sourceMetadata = $rawMetadata
        normalizedMetadata = $normalizedMetadata
        peakDbfs = Get-MaxVolumeDb -Path $normalizedPath
        activeSignalSeconds = $activeSignalSeconds
        phonemeAlphabet = 'ipa'
        phonemeValue = $ipa
    })
}

$ffmpegVersion = (& $ffmpeg.Source -version | Select-Object -First 1)
$generationConfig = [ordered]@{
    provider = 'Azure AI Speech'
    endpointVersion = 'cognitiveservices/v1'
    region = $speechRegion
    voice = $VoiceName
    modelRevision = $modelRevision
    locale = 'de-DE'
    phonemeAlphabet = 'ipa'
    ssmlRatePercent = $RatePercent
    ssml = $ssmlDescription
    outputFormat = $azureOutputFormat
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
    catalogId = [string]$catalog.id
    catalogVersion = [string]$catalog.version
    catalogSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $catalogPath).Hash.ToLowerInvariant()
    sourceGroupsFile = 'contrast-groups.csv'
    sourceGroupsSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $OutputDirectory 'contrast-groups.csv')).Hash.ToLowerInvariant()
    audioFormat = [ordered]@{
        encoding = 'PCM_SIGNED'
        sampleRate = $targetSampleRate
        bitsPerSample = 16
        channels = 1
    }
    generator = [ordered]@{
        name = 'Azure AI Speech'
        version = 'cognitiveservices/v1'
        executableSha256 = 'not-applicable-remote-api'
        modelRepository = 'Microsoft Azure AI Speech'
        modelRevision = $modelRevision
        modelName = $VoiceName
        modelSha256 = 'not-published-by-provider'
        configSha256 = Get-Sha256Text -Text $generationConfigJson
        modelLicense = 'Microsoft Azure service terms'
        datasetLicense = 'not disclosed by provider'
        syntheticVoice = $true
        noiseScale = 0
        phonemeWidthNoise = 0
        lengthScale = 1.0
        sentenceSilenceSeconds = 0
        voice = $VoiceSlug
        voiceName = $VoiceName
        aiGeneratedVoice = $true
        aiGeneratedVoiceDisclosure = $aiDisclosure
        instructions = $ssmlDescription
        phonemeAlphabet = 'ipa'
        speed = [math]::Round(1.0 + ($RatePercent / 100.0), 3)
        ssmlRatePercent = $RatePercent
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

$reviewTexts = @(
    'Bad', 'Boss', 'Butt', 'Fell', 'Grab', 'Hall', 'hin', 'hob', 'Kot', 'log',
    'Lot', 'Not', 'Ross', 'Schal', 'Tier', 'Wall', 'Weg', 'wem', 'will', 'Los'
)
$itemsByText = @{}
foreach ($target in $audioTargets) {
    $itemsByText[[string]$target.spokenText] = $target
}
$silencePath = Join-Path $OutputDirectory '_review-silence.wav'
$concatListPath = Join-Path $OutputDirectory '_review-problem-words.txt'
try {
    & $ffmpeg.Source -nostdin -hide_banner -loglevel error -y `
        -f lavfi -i "anullsrc=r=${targetSampleRate}:cl=mono" -t 0.75 -c:a pcm_s16le $silencePath
    if ($LASTEXITCODE -ne 0) {
        throw 'Vorschausilenz konnte nicht erzeugt werden.'
    }
    $concatLines = [System.Collections.Generic.List[string]]::new()
    foreach ($reviewText in $reviewTexts) {
        $matchedKey = $itemsByText.Keys | Where-Object { $_ -ceq $reviewText } | Select-Object -First 1
        if ($null -eq $matchedKey) {
            $matchedKey = $itemsByText.Keys | Where-Object { $_ -ieq $reviewText } | Select-Object -First 1
        }
        if ($null -eq $matchedKey) {
            throw "Prüfwort '$reviewText' fehlt im Katalog."
        }
        $reviewPath = Join-Path $audioDirectory "$($itemsByText[$matchedKey].id).wav"
        $concatLines.Add("file '$($reviewPath.Replace("'", "''"))'")
        $concatLines.Add("file '$($silencePath.Replace("'", "''"))'")
    }
    [System.IO.File]::WriteAllLines($concatListPath, $concatLines, $utf8WithoutBom)
    $reviewPreviewPath = Join-Path $OutputDirectory "preview-originally-problematic-$VoiceSlug.wav"
    & $ffmpeg.Source -nostdin -hide_banner -loglevel error -y `
        -f concat -safe 0 -i $concatListPath -c:a pcm_s16le $reviewPreviewPath
    if ($LASTEXITCODE -ne 0) {
        throw 'Problemwort-Vorschau konnte nicht erzeugt werden.'
    }
}
finally {
    Remove-Item -LiteralPath $silencePath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $concatListPath -Force -ErrorAction SilentlyContinue
}

[pscustomobject]@{
    OutputDirectory = $OutputDirectory
    Voice = $VoiceName
    Stimuli = $entries.Count
    MinimumActiveSignalSeconds = ($entries.activeSignalSeconds | Measure-Object -Minimum).Minimum
    ReviewPreview = (Join-Path $OutputDirectory "preview-originally-problematic-$VoiceSlug.wav")
    AudioIndex = $existingIndexPath
}
