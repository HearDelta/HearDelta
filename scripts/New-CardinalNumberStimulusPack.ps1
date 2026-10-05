[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SourceDirectory,

    [Parameter(Mandatory)]
    [string]$DestinationDirectory,

    [Parameter(Mandatory)]
    [string]$PackId,

    [Parameter(Mandatory)]
    [string]$Title
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$SourceDirectory = [IO.Path]::GetFullPath($SourceDirectory)
$DestinationDirectory = [IO.Path]::GetFullPath($DestinationDirectory)
$sourceManifest = Get-Content -LiteralPath (Join-Path $SourceDirectory 'manifest.json') -Raw | ConvertFrom-Json
if (@($sourceManifest.entries).Count -ne 900 -or $sourceManifest.synthesisMethod -ne 'ssml-say-as-cardinal') {
    throw 'Die Quelle ist kein vollständiges, per SSML-Kardinalzahl erzeugtes Zahlenmaterial.'
}

$values = @($sourceManifest.entries | ForEach-Object { [int]$_.numericValue } | Sort-Object)
if (@(Compare-Object -ReferenceObject (100..999) -DifferenceObject $values).Count -ne 0) {
    throw 'Die Quelle enthält nicht lückenlos die Werte 100 bis 999.'
}

$utf8 = [Text.UTF8Encoding]::new($false)
New-Item -ItemType Directory -Force -Path $DestinationDirectory, (Join-Path $DestinationDirectory 'audio'), (Join-Path $DestinationDirectory 'source-audio') | Out-Null

$entriesByNumber = @{}
foreach ($entry in $sourceManifest.entries) { $entriesByNumber[[int]$entry.numericValue] = $entry }
$lists = [Collections.Generic.List[object]]::new()
$audioEntries = [Collections.Generic.List[object]]::new()
for ($listIndex = 0; $listIndex -lt 36; $listIndex++) {
    $items = [Collections.Generic.List[object]]::new()
    for ($itemIndex = 0; $itemIndex -lt 25; $itemIndex++) {
        $number = 100 + $listIndex + (36 * $itemIndex)
        $source = $entriesByNumber[$number]
        $id = "cardinal-$number"
        $audioFile = "audio/$id.wav"
        $sourceAudioFile = "source-audio/$id.wav"
        Copy-Item -LiteralPath (Join-Path $SourceDirectory $source.measurementFile.Replace('/', '\')) -Destination (Join-Path $DestinationDirectory $audioFile.Replace('/', '\')) -Force
        Copy-Item -LiteralPath (Join-Path $SourceDirectory $source.rawFile.Replace('/', '\')) -Destination (Join-Path $DestinationDirectory $sourceAudioFile.Replace('/', '\')) -Force
        $items.Add([ordered]@{ id=$id; spokenText=[string]$source.cardinalText; canonicalResponse=[string]$number; audioFile=$audioFile })
        $audioEntries.Add([ordered]@{
            stimulusId=$id; audioFile=$audioFile; sha256=[string]$source.measurementSha256
            byteLength=(Get-Item -LiteralPath (Join-Path $DestinationDirectory $audioFile.Replace('/', '\'))).Length
            sourceAudioFile=$sourceAudioFile; sourceSha256=[string]$source.rawSha256
            sourceByteLength=(Get-Item -LiteralPath (Join-Path $DestinationDirectory $sourceAudioFile.Replace('/', '\'))).Length
        })
    }
    $lists.Add([ordered]@{ id=('numbers-{0:00}' -f ($listIndex + 1)); material='numbers'; items=@($items) })
}

$catalog = [ordered]@{
    schemaVersion=1; id=$PackId; version='1.0.0'; language='de-DE'; title=$Title; license='Azure AI Speech service output; private personal use'; clinicallyValidated=$false
    itemsPerList=25; lists=@($lists); paradigm='open-set-cardinal-number'; responseAlternativeCount=$null; chanceLevelPercent=0.1111111111111111
}
$catalogPath = Join-Path $DestinationDirectory 'catalog.json'
[IO.File]::WriteAllText($catalogPath, (($catalog | ConvertTo-Json -Depth 8) + [Environment]::NewLine), $utf8)
$catalogHash = (Get-FileHash -LiteralPath $catalogPath -Algorithm SHA256).Hash.ToLowerInvariant()
$voice = [string]$sourceManifest.voice
$voiceLabel = [string]$sourceManifest.voiceDisplayName
$audioIndex = [ordered]@{
    schemaVersion=1; catalogId=$PackId; catalogVersion='1.0.0'; catalogSha256=$catalogHash
    audioFormat=[ordered]@{ encoding='PCM_SIGNED'; sampleRate=22050; bitsPerSample=16; channels=1 }
    generator=[ordered]@{
        name='Azure AI Speech'; version='Speech REST v1'; executableSha256=('0' * 64); modelRepository='Azure AI Speech'; modelRevision=[string]$sourceManifest.modelRevision
        modelName=$voice; modelSha256=('0' * 64); configSha256=('0' * 64); modelLicense='Azure AI Speech service terms'; datasetLicense='not applicable'
        syntheticVoice=$true; noiseScale=0; phonemeWidthNoise=0; lengthScale=1; sentenceSilenceSeconds=0; voice=$voice; aiGeneratedVoice=$true
        aiGeneratedVoiceDisclosure="Alle Audiodateien dieses Pakets enthalten die KI-generierte Azure-Stimme $voiceLabel und keine menschliche Aufnahme."
        instructions='SSML say-as interpret-as=cardinal; German three-digit cardinal numbers 100 to 999; rate -30 percent.'; speed=0.7
        responseFormat='open-set-three-digit-number'; sourceAudioFormat='PCM16 mono 24000 Hz'; normalization='PCM16 mono 22050 Hz; sample peak -6.0 dBFS'; generatedAt=[string]$sourceManifest.generatedAtUtc
    }
    entries=@($audioEntries)
}
[IO.File]::WriteAllText((Join-Path $DestinationDirectory 'audio-index.json'), (($audioIndex | ConvertTo-Json -Depth 8) + [Environment]::NewLine), $utf8)
$readme = @"
# $Title

900 deutsche Kardinalzahlen von 100 bis 999, verteilt auf 36 getrennte Listen
zu je 25 Zahlen. Die Antwort erfolgt als freie dreistellige Zahl; die
Zufallstrefferquote beträgt 1/900 (0,111... Prozent). Alle Audiodateien
enthalten die KI-generierte Azure-Stimme $voiceLabel. Dieses Material ist
nicht klinisch validiert. Roh- und Mess-WAVs, Katalog und Audioindex sind
hashgebunden und Teil der Materialidentität.
"@
[IO.File]::WriteAllText((Join-Path $DestinationDirectory 'README.md'), $readme, $utf8)
Write-Host "Kardinalzahl-Stimuluspaket erzeugt: $DestinationDirectory"
