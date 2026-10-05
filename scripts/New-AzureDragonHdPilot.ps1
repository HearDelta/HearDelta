[CmdletBinding()]
param(
    [string]$ContrastGroupsPath = (Join-Path $PSScriptRoot '..\stimuli\de-DE\personal-phoneme-contrast-v1\contrast-groups.csv'),

    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\diagnostics\audio\azure-dragonhd-de-pilot-2026-09-03'),

    [ValidateSet('florian', 'seraphina', 'conrad', 'katja', 'bernd', 'christoph', 'kasper', 'killian', 'klaus', 'ralf', 'klausmai')]
    [string[]]$Voices = @('florian', 'seraphina'),

    [string[]]$StimulusTexts = @(),

    [ValidateSet('ipa', 'sapi')]
    [string]$PhonemeAlphabet = 'ipa',

    [switch]$GermanContext,

    [switch]$SemanticContext,

    [switch]$GermanRespelling,

    [ValidateRange(1.0, 3.0)]
    [double]$ContextBreakSeconds = 1.5,

    [ValidateRange(-50, 100)]
    [int]$RatePercent = 0,

    [switch]$ForceNormalization,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

$subscriptionKey = [Environment]::GetEnvironmentVariable('AZURE_SPEECH_KEY')
if ([string]::IsNullOrWhiteSpace($subscriptionKey)) {
    throw 'AZURE_SPEECH_KEY ist nicht gesetzt.'
}

$speechRegion = [Environment]::GetEnvironmentVariable('AZURE_SPEECH_REGION')
if ([string]::IsNullOrWhiteSpace($speechRegion)) {
    throw 'AZURE_SPEECH_REGION ist nicht gesetzt.'
}
if ($speechRegion -notmatch '^[a-z0-9]+$') {
    throw 'AZURE_SPEECH_REGION hat ein unerwartetes Format.'
}

$ffmpeg = Get-Command ffmpeg -ErrorAction Stop
$ffprobe = Get-Command ffprobe -ErrorAction Stop
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
$invariantCulture = [System.Globalization.CultureInfo]::InvariantCulture
$targetPeakDbfs = -6.0
$peakToleranceDb = 0.25
$targetSampleRate = 22050
$azureOutputFormat = 'riff-24khz-16bit-mono-pcm'
$speechEndpoint = "https://$speechRegion.tts.speech.microsoft.com/cognitiveservices/v1"

$voiceDefinitions = @{
    florian = [ordered]@{
        Name = 'de-DE-Florian:DragonHDLatestNeural'
        Model = 'DragonHDLatestNeural'
        SupportsTemperature = $true
        SupportsProsody = $false
        Style = $null
    }
    seraphina = [ordered]@{
        Name = 'de-DE-Seraphina:DragonHDLatestNeural'
        Model = 'DragonHDLatestNeural'
        SupportsTemperature = $true
        SupportsProsody = $false
        Style = $null
    }
    conrad = [ordered]@{
        Name = 'de-DE-ConradNeural'
        Model = 'StandardNeural'
        SupportsTemperature = $false
        SupportsProsody = $true
        Style = $null
    }
    katja = [ordered]@{
        Name = 'de-DE-KatjaNeural'
        Model = 'StandardNeural'
        SupportsTemperature = $false
        SupportsProsody = $true
        Style = $null
    }
    bernd = [ordered]@{
        Name = 'de-DE-BerndNeural'
        Model = 'StandardNeural'
        SupportsTemperature = $false
        SupportsProsody = $true
        Style = $null
    }
    christoph = [ordered]@{
        Name = 'de-DE-ChristophNeural'
        Model = 'StandardNeural'
        SupportsTemperature = $false
        SupportsProsody = $true
        Style = $null
    }
    kasper = [ordered]@{
        Name = 'de-DE-KasperNeural'
        Model = 'StandardNeural'
        SupportsTemperature = $false
        SupportsProsody = $true
        Style = $null
    }
    killian = [ordered]@{
        Name = 'de-DE-KillianNeural'
        Model = 'StandardNeural'
        SupportsTemperature = $false
        SupportsProsody = $true
        Style = $null
    }
    klaus = [ordered]@{
        Name = 'de-DE-KlausNeural'
        Model = 'StandardNeural'
        SupportsTemperature = $false
        SupportsProsody = $true
        Style = $null
    }
    ralf = [ordered]@{
        Name = 'de-DE-RalfNeural'
        Model = 'StandardNeural'
        SupportsTemperature = $false
        SupportsProsody = $true
        Style = $null
    }
    klausmai = [ordered]@{
        Name = 'de-DE-Klaus:MAI-Voice-2'
        Model = 'MAI-Voice-2'
        SupportsTemperature = $false
        SupportsProsody = $false
        Style = 'softvoice'
    }
}

$semanticContextByText = @{
    log = 'Er sagte in der Vergangenheit nicht die Wahrheit; er log.'
    Kot = 'Der Hund hinterließ Kot.'
}
$germanRespellingByText = @{
    log = 'lohg'
    Kot = 'Koht'
}

# These are the stimuli that were reported as English or empty in the Cedar
# pack. IPA is copied from the versioned contrast-group source and is sent via
# SSML so an isolated spelling cannot trigger English pronunciation.
$stimuli = @(
    [pscustomobject]@{ Text = 'Bad';  Ipa = 'baːt';  Sapi = 'b a: 1 t' },
    [pscustomobject]@{ Text = 'Boss'; Ipa = 'bɔs';   Sapi = 'b oh 1 s' },
    [pscustomobject]@{ Text = 'Butt'; Ipa = 'bʊt';   Sapi = 'b uh 1 t' },
    [pscustomobject]@{ Text = 'Fell'; Ipa = 'fɛl';   Sapi = 'f eh 1 l' },
    [pscustomobject]@{ Text = 'Grab'; Ipa = 'ɡʁaːp'; Sapi = 'g r a: 1 p' },
    [pscustomobject]@{ Text = 'Hall'; Ipa = 'hal';   Sapi = 'h a 1 l' },
    [pscustomobject]@{ Text = 'hin';  Ipa = 'hɪn';   Sapi = 'h ih 1 n' },
    [pscustomobject]@{ Text = 'hob';  Ipa = 'hoːp';  Sapi = 'h ow 1 p' },
    [pscustomobject]@{ Text = 'Kot';  Ipa = 'koːt';  Sapi = 'k ow 1 t' },
    [pscustomobject]@{ Text = 'log';  Ipa = 'loːk';  Sapi = 'l ow 1 k' },
    [pscustomobject]@{ Text = 'Lot';  Ipa = 'loːt';  Sapi = 'l ow 1 t' },
    [pscustomobject]@{ Text = 'Not';  Ipa = 'noːt';  Sapi = 'n ow 1 t' },
    [pscustomobject]@{ Text = 'Ross'; Ipa = 'ʁɔs';   Sapi = 'r oh 1 s' },
    [pscustomobject]@{ Text = 'Schal'; Ipa = 'ʃaːl'; Sapi = 'sh a: 1 l' },
    [pscustomobject]@{ Text = 'Tier'; Ipa = 'tiːɐ̯'; Sapi = 't iy 1 ax r' },
    [pscustomobject]@{ Text = 'Wall'; Ipa = 'val';   Sapi = 'v a 1 l' },
    [pscustomobject]@{ Text = 'Weg';  Ipa = 'veːk';  Sapi = 'v ey 1 k' },
    [pscustomobject]@{ Text = 'wem';  Ipa = 'veːm';  Sapi = 'v ey 1 m' },
    [pscustomobject]@{ Text = 'will'; Ipa = 'vɪl';   Sapi = 'v ih 1 l' },
    [pscustomobject]@{ Text = 'Los';  Ipa = 'loːs';  Sapi = 'l ow 1 s' }
)

if (-not (Test-Path -LiteralPath $ContrastGroupsPath -PathType Leaf)) {
    throw "Phonemkontrastquelle fehlt: $ContrastGroupsPath"
}
$catalogIpaByText = @{}
foreach ($group in (Import-Csv -LiteralPath $ContrastGroupsPath -Delimiter ';')) {
    foreach ($alternativeNumber in 1..5) {
        $catalogText = [string]$group."alternative_$alternativeNumber"
        $catalogIpa = ([string]$group."ipa_$alternativeNumber").Trim('/')
        $catalogIpaByText[$catalogText] = $catalogIpa
    }
}
foreach ($stimulus in $stimuli) {
    if (-not $catalogIpaByText.ContainsKey($stimulus.Text)) {
        throw "Pilotwort '$($stimulus.Text)' fehlt in der Phonemkontrastquelle."
    }
    if ($catalogIpaByText[$stimulus.Text] -cne $stimulus.Ipa) {
        throw "IPA für '$($stimulus.Text)' stimmt nicht mit der Phonemkontrastquelle überein."
    }
}
if ($StimulusTexts.Count -gt 0) {
    $stimuliByText = @{}
    foreach ($stimulus in $stimuli) {
        $stimuliByText[$stimulus.Text] = $stimulus
    }
    $selectedStimuli = foreach ($stimulusText in $StimulusTexts) {
        if (-not $stimuliByText.ContainsKey($stimulusText)) {
            throw "Unbekanntes Pilotwort: $stimulusText"
        }
        $stimuliByText[$stimulusText]
    }
    $stimuli = @($selectedStimuli)
}
if ($GermanContext -and $SemanticContext) {
    throw 'GermanContext und SemanticContext dürfen nicht gleichzeitig gesetzt sein.'
}
if ($SemanticContext) {
    foreach ($stimulus in $stimuli) {
        if (-not $semanticContextByText.ContainsKey($stimulus.Text)) {
            throw "Für '$($stimulus.Text)' ist kein semantischer Kontext definiert."
        }
    }
}
if ($GermanRespelling) {
    foreach ($stimulus in $stimuli) {
        if (-not $germanRespellingByText.ContainsKey($stimulus.Text)) {
            throw "Für '$($stimulus.Text)' ist keine deutsche Hilfsschreibung definiert."
        }
    }
}

function Invoke-AzureSpeech {
    param(
        [Parameter(Mandatory)][string]$VoiceName,
        [Parameter(Mandatory)][bool]$SupportsTemperature,
        [Parameter(Mandatory)][bool]$SupportsProsody,
        [AllowNull()][string]$Style,
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Ipa,
        [Parameter(Mandatory)][string]$Sapi,
        [Parameter(Mandatory)][string]$DestinationPath
    )

    $synthesisText = if ($GermanRespelling) { $germanRespellingByText[$Text] } else { $Text }
    $escapedText = [System.Security.SecurityElement]::Escape($synthesisText)
    $phonemeValue = if ($PhonemeAlphabet -eq 'sapi') { $Sapi } else { $Ipa }
    $escapedPhoneme = [System.Security.SecurityElement]::Escape($phonemeValue)
    $targetSsml = "<lang xml:lang=`"de-DE`"><phoneme alphabet=`"$PhonemeAlphabet`" ph=`"$escapedPhoneme`">$escapedText</phoneme></lang>"
    $speechBody = $targetSsml
    if ($GermanContext) {
        $contextBreakMilliseconds = [int][math]::Round($ContextBreakSeconds * 1000)
        $speechBody = "Das folgende Wort ist ein deutsches Wort und wird deutsch ausgesprochen.<break time=`"${contextBreakMilliseconds}ms`"/>$targetSsml"
    }
    elseif ($SemanticContext) {
        $contextBreakMilliseconds = [int][math]::Round($ContextBreakSeconds * 1000)
        $escapedContext = [System.Security.SecurityElement]::Escape($semanticContextByText[$Text])
        $speechBody = "$escapedContext<break time=`"${contextBreakMilliseconds}ms`"/>$targetSsml"
    }
    if ($RatePercent -ne 0 -and $SupportsProsody) {
        $rateValue = if ($RatePercent -gt 0) { "+$RatePercent%" } else { "$RatePercent%" }
        $speechBody = "<prosody rate=`"$rateValue`">$speechBody</prosody>"
    }
    if (-not [string]::IsNullOrWhiteSpace($Style)) {
        $escapedStyle = [System.Security.SecurityElement]::Escape($Style)
        $speechBody = "<mstts:express-as style=`"$escapedStyle`">$speechBody</mstts:express-as>"
    }
    $voiceAttributes = if ($SupportsTemperature) { ' parameters="temperature=0"' } else { '' }
    $ssml = @"
<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xmlns:mstts="http://www.w3.org/2001/mstts" xml:lang="de-DE">
  <voice name="$VoiceName"$voiceAttributes>
    $speechBody
  </voice>
</speak>
"@
    $requestBytes = $utf8WithoutBom.GetBytes($ssml)
    $headers = @{
        'Ocp-Apim-Subscription-Key' = $subscriptionKey
        'X-Microsoft-OutputFormat' = $azureOutputFormat
        'User-Agent' = 'HearDelta-AzureSpeechPilot'
    }

    for ($attempt = 1; $attempt -le 2; $attempt++) {
        try {
            Invoke-WebRequest `
                -Uri $speechEndpoint `
                -Method Post `
                -Headers $headers `
                -ContentType 'application/ssml+xml; charset=utf-8' `
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
        throw "Spitzenpegel konnte nicht bestimmt werden; die Datei ist möglicherweise leer: $Path"
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

function Get-ContextTargetStartSeconds {
    param([Parameter(Mandatory)][string]$Path)

    $analysis = & $ffmpeg.Source `
        -nostdin `
        -hide_banner `
        -i $Path `
        -af 'silencedetect=noise=-45dB:d=0.8' `
        -f null `
        NUL 2>&1
    $matches = [regex]::Matches(
        ($analysis -join "`n"),
        'silence_end:\s*(?<end>[0-9]+(?:\.[0-9]+)?)\s*\|\s*silence_duration:\s*(?<duration>[0-9]+(?:\.[0-9]+)?)')
    $silences = foreach ($match in $matches) {
        [pscustomobject]@{
            End = [double]::Parse($match.Groups['end'].Value, $invariantCulture)
            Duration = [double]::Parse($match.Groups['duration'].Value, $invariantCulture)
        }
    }
    $contextSilence = $silences |
        Where-Object Duration -ge 0.8 |
        Sort-Object Duration -Descending |
        Select-Object -First 1
    if ($null -eq $contextSilence) {
        throw "Die erwartete Kontextpause wurde nicht gefunden: $Path"
    }

    # Preserve 50 ms of the forced pause to avoid clipping the word onset.
    return [math]::Round([math]::Max(0, $contextSilence.End - 0.05), 3)
}

function Convert-ToPilotFormat {
    param(
        [Parameter(Mandatory)][string]$SourcePath,
        [Parameter(Mandatory)][string]$DestinationPath,
        [double]$StartSeconds = 0
    )

    $convertedPath = "$DestinationPath.pre-normalize.wav"
    try {
        $conversionArguments = @(
            '-nostdin',
            '-hide_banner',
            '-loglevel', 'error',
            '-y',
            '-i', $SourcePath
        )
        if ($StartSeconds -gt 0) {
            $conversionArguments += @('-ss', $StartSeconds.ToString('0.000', $invariantCulture))
        }
        $conversionArguments += @(
            '-ar', $targetSampleRate,
            '-ac', '1',
            '-c:a', 'pcm_s16le',
            $convertedPath
        )
        & $ffmpeg.Source @conversionArguments
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
$entries = [System.Collections.Generic.List[object]]::new()
$silencePath = Join-Path $OutputDirectory '_preview-silence.wav'
if (-not (Test-Path -LiteralPath $silencePath -PathType Leaf)) {
    & $ffmpeg.Source -nostdin -hide_banner -loglevel error -y `
        -f lavfi -i "anullsrc=r=${targetSampleRate}:cl=mono" -t 0.75 -c:a pcm_s16le $silencePath
    if ($LASTEXITCODE -ne 0) {
        throw 'Vorschausilenz konnte nicht erzeugt werden.'
    }
}

foreach ($voice in $Voices) {
    $voiceDefinition = $voiceDefinitions[$voice]
    $voiceName = [string]$voiceDefinition.Name
    $rawDirectory = Join-Path $OutputDirectory "raw\$voice"
    $normalizedDirectory = Join-Path $OutputDirectory "normalized\$voice"
    New-Item -ItemType Directory -Path $rawDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $normalizedDirectory -Force | Out-Null

    foreach ($stimulus in $stimuli) {
        $rawPath = Join-Path $rawDirectory "$($stimulus.Text).wav"
        $normalizedPath = Join-Path $normalizedDirectory "$($stimulus.Text).wav"

        if ($Force -or -not (Test-Path -LiteralPath $rawPath -PathType Leaf)) {
            Invoke-AzureSpeech `
                -VoiceName $voiceName `
                -SupportsTemperature ([bool]$voiceDefinition.SupportsTemperature) `
                -SupportsProsody ([bool]$voiceDefinition.SupportsProsody) `
                -Style ([string]$voiceDefinition.Style) `
                -Text $stimulus.Text `
                -Ipa $stimulus.Ipa `
                -Sapi $stimulus.Sapi `
                -DestinationPath $rawPath
        }
        $targetStartSeconds = if ($GermanContext -or $SemanticContext) {
            Get-ContextTargetStartSeconds -Path $rawPath
        }
        else {
            0
        }
        if ($Force -or $ForceNormalization -or -not (Test-Path -LiteralPath $normalizedPath -PathType Leaf)) {
            Convert-ToPilotFormat `
                -SourcePath $rawPath `
                -DestinationPath $normalizedPath `
                -StartSeconds $targetStartSeconds
        }

        $rawMetadata = Get-AudioMetadata -Path $rawPath
        $normalizedMetadata = Get-AudioMetadata -Path $normalizedPath
        if ($rawMetadata.codec -ne 'pcm_s16le' -or
            $rawMetadata.sampleRate -ne 24000 -or
            $rawMetadata.channels -ne 1 -or
            $rawMetadata.bitsPerSample -ne 16) {
            throw "Azure-Rohformat stimmt für '$rawPath' nicht."
        }
        if ($normalizedMetadata.codec -ne 'pcm_s16le' -or
            $normalizedMetadata.sampleRate -ne $targetSampleRate -or
            $normalizedMetadata.channels -ne 1 -or
            $normalizedMetadata.bitsPerSample -ne 16) {
            throw "Pilotformat stimmt für '$normalizedPath' nicht."
        }

        $entries.Add([ordered]@{
            voice = $voice
            voiceName = $voiceName
            model = [string]$voiceDefinition.Model
            text = $stimulus.Text
            synthesisText = if ($GermanRespelling) { $germanRespellingByText[$stimulus.Text] } else { $stimulus.Text }
            ipa = $stimulus.Ipa
            sapi = $stimulus.Sapi
            phonemeAlphabet = $PhonemeAlphabet
            phonemeValue = if ($PhonemeAlphabet -eq 'sapi') { $stimulus.Sapi } else { $stimulus.Ipa }
            ssmlTemperature = if ($voiceDefinition.SupportsTemperature) { 0 } else { $null }
            ssmlRatePercent = if ($voiceDefinition.SupportsProsody) { $RatePercent } else { $null }
            ssmlStyle = [string]$voiceDefinition.Style
            germanContext = [bool]$GermanContext
            semanticContext = [bool]$SemanticContext
            germanRespelling = [bool]$GermanRespelling
            contextCropStartSeconds = $targetStartSeconds
            raw = [ordered]@{
                file = [System.IO.Path]::GetRelativePath($OutputDirectory, $rawPath).Replace('\', '/')
                sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $rawPath).Hash.ToLowerInvariant()
                peakDbfs = Get-MaxVolumeDb -Path $rawPath
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

    $concatListPath = Join-Path $OutputDirectory "_preview-$voice.txt"
    try {
        $concatLines = [System.Collections.Generic.List[string]]::new()
        foreach ($stimulus in $stimuli) {
            $normalizedPath = Join-Path $normalizedDirectory "$($stimulus.Text).wav"
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
    finally {
        Remove-Item -LiteralPath $concatListPath -Force -ErrorAction SilentlyContinue
    }
}

Remove-Item -LiteralPath $silencePath -Force -ErrorAction SilentlyContinue

$manifest = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    provider = 'Azure AI Speech'
    serviceRegion = $speechRegion
    models = @($Voices | ForEach-Object { [string]$voiceDefinitions[$_].Model } | Select-Object -Unique)
    inputLocale = 'de-DE'
    inputMode = if ($SemanticContext) {
        "Semantic German context, forced pause, SSML $PhonemeAlphabet phoneme, cropped target"
    }
    elseif ($GermanContext) {
        "Generic German context, forced pause, SSML $PhonemeAlphabet phoneme, cropped target"
    }
    elseif ($GermanRespelling) {
        "German respelling, SSML $PhonemeAlphabet phoneme"
    }
    else {
        "SSML $PhonemeAlphabet phoneme"
    }
    phonemeAlphabet = $PhonemeAlphabet
    ratePercent = $RatePercent
    contextBreakSeconds = if ($GermanContext -or $SemanticContext) { $ContextBreakSeconds } else { $null }
    rawOutputFormat = $azureOutputFormat
    normalization = [ordered]@{
        sampleRate = $targetSampleRate
        channels = 1
        bitsPerSample = 16
        codec = 'pcm_s16le'
        targetPeakDbfs = $targetPeakDbfs
    }
    entries = $entries
}
$manifestPath = Join-Path $OutputDirectory 'manifest.json'
[System.IO.File]::WriteAllText(
    $manifestPath,
    ($manifest | ConvertTo-Json -Depth 10),
    $utf8WithoutBom)

[pscustomobject]@{
    OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
    Voices = ($Voices -join ', ')
    StimuliPerVoice = $stimuli.Count
    Files = $entries.Count
    Manifest = (Resolve-Path -LiteralPath $manifestPath).Path
}
