[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\diagnostics\audio\azure-cardinal-number-pilot-2026-09-08'),

    [string]$VoiceName = 'de-DE-RalfNeural',

    [ValidateRange(-50, 100)]
    [int]$RatePercent = -30,

    [ValidateRange(100, 999)]
    [int[]]$Numbers = @(100, 111, 222, 345, 987, 404, 440, 707, 190, 618, 253, 876),

    [switch]$UseSayAsCardinal,

    [string]$VoiceDisplayName = '',

    [switch]$SkipPreview,

    [switch]$ForceApi
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($UseSayAsCardinal -and -not $PSBoundParameters.ContainsKey('OutputDirectory')) {
    $OutputDirectory = Join-Path $PSScriptRoot '..\diagnostics\audio\azure-cardinal-number-say-as-pilot-2026-09-08'
}

function Resolve-RequiredTool([string]$Name) {
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command }
    $wingetPackages = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    $candidate = Get-ChildItem -LiteralPath $wingetPackages -Filter "$Name.exe" -File -Recurse `
        -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $candidate) { throw "Erforderliches Werkzeug fehlt: $Name." }
    [pscustomobject]@{ Source = $candidate.FullName }
}

function ConvertTo-GermanCardinal([int]$Number) {
    if ($Number -lt 100 -or $Number -gt 999) {
        throw 'Der Kardinalpilot akzeptiert nur dreistellige Zahlen von 100 bis 999.'
    }

    $units = @('null', 'eins', 'zwei', 'drei', 'vier', 'fünf', 'sechs', 'sieben', 'acht', 'neun')
    $teens = @{ 10 = 'zehn'; 11 = 'elf'; 12 = 'zwölf'; 13 = 'dreizehn'; 14 = 'vierzehn'; 15 = 'fünfzehn'; 16 = 'sechzehn'; 17 = 'siebzehn'; 18 = 'achtzehn'; 19 = 'neunzehn' }
    $tens = @{ 20 = 'zwanzig'; 30 = 'dreißig'; 40 = 'vierzig'; 50 = 'fünfzig'; 60 = 'sechzig'; 70 = 'siebzig'; 80 = 'achtzig'; 90 = 'neunzig' }

    $hundreds = [math]::Floor($Number / 100)
    $remainder = $Number % 100
    $prefix = if ($hundreds -eq 1) { 'einhundert' } else { "{0}hundert" -f $units[$hundreds] }
    if ($remainder -eq 0) { return $prefix }
    if ($remainder -lt 10) { return "$prefix$($units[$remainder])" }
    if ($remainder -lt 20) { return "$prefix$($teens[$remainder])" }

    $unit = $remainder % 10
    $tensPart = $tens[$remainder - $unit]
    if ($unit -eq 0) { return "$prefix$tensPart" }
    $unitPart = if ($unit -eq 1) { 'ein' } else { $units[$unit] }
    return $prefix + $unitPart + 'und' + $tensPart
}

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
    $ffmpeg = Resolve-RequiredTool 'ffmpeg'
    $ffprobe = Resolve-RequiredTool 'ffprobe'
    $utf8 = [Text.UTF8Encoding]::new($false)
    $invariantCulture = [Globalization.CultureInfo]::InvariantCulture
    $targetSampleRate = 22050
    $targetPeakDbfs = -6.0
    $rateValue = if ($RatePercent -gt 0) { "+${RatePercent}%" } else { "${RatePercent}%" }
    if ([string]::IsNullOrWhiteSpace($VoiceDisplayName)) {
        $VoiceDisplayName = (($VoiceName -replace '^de-DE-', '') -replace '(Multilingual)?Neural$', '')
    }
    $ttsEndpoint = "https://${speechRegion}.tts.speech.microsoft.com/cognitiveservices/v1"
    # Each value is three-digit and covers repetitions, rising/falling patterns and every digit.
    $numbers = @($Numbers)

    function Invoke-AzureSpeech([string]$SpeechMarkup, [string]$DestinationPath) {
        $ssml = @"
<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="de-DE">
  <voice name="$VoiceName"><prosody rate="$rateValue"><lang xml:lang="de-DE">$SpeechMarkup</lang></prosody></voice>
</speak>
"@
        $temporaryPath = "$DestinationPath.download"
        $headers = @{
            'Ocp-Apim-Subscription-Key' = $subscriptionKey
            'X-Microsoft-OutputFormat' = 'riff-24khz-16bit-mono-pcm'
            'User-Agent' = 'HearDelta-AzureCardinalNumberPilot'
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
        finally { Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue }
    }

    function Assert-Format([string]$Path, [int]$ExpectedSampleRate) {
        $metadata = (& $ffprobe.Source -v error -select_streams a:0 `
            -show_entries stream=codec_name,sample_rate,channels,bits_per_sample -of json $Path | ConvertFrom-Json).streams[0]
        if ($metadata.codec_name -ne 'pcm_s16le' -or [int]$metadata.sample_rate -ne $ExpectedSampleRate -or
            [int]$metadata.channels -ne 1 -or [int]$metadata.bits_per_sample -ne 16) {
            throw "Unerwartetes Audioformat: $Path"
        }
    }

    function Convert-ToMeasurementFormat([string]$SourcePath, [string]$DestinationPath) {
        $temporaryPath = "$DestinationPath.pre-normalize.wav"
        try {
            & $ffmpeg.Source -nostdin -hide_banner -loglevel error -y -i $SourcePath `
                -ar $targetSampleRate -ac 1 -c:a pcm_s16le $temporaryPath
            if ($LASTEXITCODE -ne 0) { throw "Audio-Konvertierung fehlgeschlagen: $SourcePath" }
            $volumeOutput = & $ffmpeg.Source -nostdin -hide_banner -i $temporaryPath -af volumedetect -f null NUL 2>&1
            $match = [regex]::Match(($volumeOutput -join "`n"), 'max_volume:\s*(-?[0-9]+(?:\.[0-9]+)?)\s*dB')
            if (-not $match.Success) { throw "Spitzenpegel nicht bestimmbar: $SourcePath" }
            $gain = $targetPeakDbfs - [double]::Parse($match.Groups[1].Value, $invariantCulture)
            & $ffmpeg.Source -nostdin -hide_banner -loglevel error -y -i $temporaryPath `
                -af "volume=$($gain.ToString('0.00', $invariantCulture))dB" -c:a pcm_s16le $DestinationPath
            if ($LASTEXITCODE -ne 0) { throw "Audio-Normalisierung fehlgeschlagen: $SourcePath" }
        }
        finally { Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue }
    }

    New-Item -ItemType Directory -Path $OutputDirectory, (Join-Path $OutputDirectory 'raw'), (Join-Path $OutputDirectory 'normalized') -Force | Out-Null
    $entries = [Collections.Generic.List[object]]::new()
    $numberIndex = 0
    $totalNumbers = $numbers.Count
    foreach ($number in $numbers) {
        $numberIndex++
        $id = "cn-${number}"
        $cardinal = ConvertTo-GermanCardinal $number
        $rawPath = Join-Path $OutputDirectory "raw\${id}.wav"
        $measurementPath = Join-Path $OutputDirectory "normalized\${id}.wav"
        if ($ForceApi -or -not (Test-Path -LiteralPath $rawPath -PathType Leaf)) {
            if ($totalNumbers -le 25 -or $numberIndex -eq 1 -or $numberIndex % 25 -eq 0 -or $numberIndex -eq $totalNumbers) {
                Write-Host ("Azure Kardinalzahl [{0}/{1}]: {2} ({3})" -f $numberIndex, $totalNumbers, $number, $cardinal)
            }
            $speechMarkup = if ($UseSayAsCardinal) {
                "<say-as interpret-as=`"cardinal`">$number</say-as>"
            }
            else {
                [Security.SecurityElement]::Escape($cardinal)
            }
            Invoke-AzureSpeech -SpeechMarkup $speechMarkup -DestinationPath $rawPath
        }
        Assert-Format -Path $rawPath -ExpectedSampleRate 24000
        if ($ForceApi -or -not (Test-Path -LiteralPath $measurementPath -PathType Leaf)) {
            Convert-ToMeasurementFormat -SourcePath $rawPath -DestinationPath $measurementPath
        }
        Assert-Format -Path $measurementPath -ExpectedSampleRate $targetSampleRate
        $entries.Add([ordered]@{
            id = $id; numericValue = $number; cardinalText = $cardinal
            rawFile = "raw/${id}.wav"; rawSha256 = (Get-FileHash -LiteralPath $rawPath -Algorithm SHA256).Hash.ToLowerInvariant()
            measurementFile = "normalized/${id}.wav"; measurementSha256 = (Get-FileHash -LiteralPath $measurementPath -Algorithm SHA256).Hash.ToLowerInvariant()
        })
    }

    if (-not $SkipPreview) {
        $silencePath = Join-Path $OutputDirectory '_preview-silence.wav'
        $concatListPath = Join-Path $OutputDirectory '_preview-cardinal.txt'
        try {
            & $ffmpeg.Source -nostdin -hide_banner -loglevel error -y -f lavfi -i "anullsrc=r=${targetSampleRate}:cl=mono" -t 0.75 -c:a pcm_s16le $silencePath
            if ($LASTEXITCODE -ne 0) { throw 'Vorschausilenz konnte nicht erzeugt werden.' }
            $lines = foreach ($entry in $entries) {
                "file '$((Join-Path $OutputDirectory $entry.measurementFile).Replace("'", "''"))'"
                "file '$($silencePath.Replace("'", "''"))'"
            }
            [IO.File]::WriteAllLines($concatListPath, @($lines), $utf8)
            $previewPath = Join-Path $OutputDirectory 'preview-cardinal.wav'
            & $ffmpeg.Source -nostdin -hide_banner -loglevel error -y -f concat -safe 0 -i $concatListPath -c:a pcm_s16le $previewPath
            if ($LASTEXITCODE -ne 0) { throw 'Kardinalzahl-Vorschau konnte nicht erzeugt werden.' }
        }
        finally {
            Remove-Item -LiteralPath $silencePath, $concatListPath -Force -ErrorAction SilentlyContinue
        }
    }

    $manifest = [ordered]@{
        schemaVersion = 1
        purpose = if ($numbers.Count -eq 900) { 'Candidate German cardinal-number material for three-digit values; not clinical or validated test material.' } else { 'Experimental German cardinal-number pilot for three-digit values; not clinical or validated test material.' }
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        provider = 'Azure AI Speech'; voice = $VoiceName; voiceDisplayName = $VoiceDisplayName; modelRevision = 'service-managed-current-2026-09-08'
        locale = 'de-DE'; ratePercent = $RatePercent
        synthesisMethod = if ($UseSayAsCardinal) { 'ssml-say-as-cardinal' } else { 'plain-cardinal-text' }
        numberRange = '100..999; no leading zero values'
        measurementFormat = 'PCM16 mono 22050 Hz, sample peak -6.0 dBFS'
        previewGenerated = -not $SkipPreview
        aiGeneratedVoiceDisclosure = "Alle Audiodateien dieses Materials enthalten die KI-generierte Azure-Stimme $VoiceDisplayName und keine menschliche Aufnahme."
        entries = @($entries)
    }
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'manifest.json'), (($manifest | ConvertTo-Json -Depth 8) + [Environment]::NewLine), $utf8)
    $readme = @"
# Azure-Kardinalzahl-Material

Dieser Pilot spricht dreistellige Zahlen als deutsche Kardinalzahlen, etwa
100 als einhundert und 440 als vierhundertvierzig. Die Azure-Anfrage
verwendet $(if ($UseSayAsCardinal) { 'SSML say-as mit interpret-as="cardinal"' } else { 'vorab ausgeschriebenen Kardinaltext' }). Es enthält keine
Werte mit führenden Nullen, weil diese als Kardinalzahl nicht hörbar eindeutig
wären. Alle Mess-WAVs sind PCM16, mono, 22,05 kHz und auf -6,0 dBFS
Spitzenwert normalisiert. Die Stimme ist $VoiceDisplayName. Das Material ist nicht klinisch validiert und nicht Teil
des aktiven Stimuluspakets. Eine Gesamtvorschau wurde $(if ($SkipPreview) { 'nicht' } else { '' }) erzeugt.
"@
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'README.md'), $readme, $utf8)
    Write-Host "Kardinalzahl-Pilot erzeugt: $OutputDirectory"
}
finally {
    $subscriptionKey = $null
}
