[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\diagnostics\audio\azure-cardinal-voice-comparison-2026-09-08'),

    [ValidateRange(-50, 100)]
    [int]$RatePercent = -30,

    [ValidateRange(100, 999)]
    [int[]]$Numbers = @(100, 190, 345, 440, 618, 876)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$generatorPath = Join-Path $PSScriptRoot 'New-AzureCardinalNumberPilot.ps1'
if (-not (Test-Path -LiteralPath $generatorPath -PathType Leaf)) {
    throw "Kardinalzahl-Generator fehlt: $generatorPath"
}

$voices = @(
    [ordered]@{ slug = 'bernd'; displayName = 'Bernd'; gender = 'männlich'; voiceName = 'de-DE-BerndNeural' },
    [ordered]@{ slug = 'christoph'; displayName = 'Christoph'; gender = 'männlich'; voiceName = 'de-DE-ChristophNeural' },
    [ordered]@{ slug = 'conrad'; displayName = 'Conrad'; gender = 'männlich'; voiceName = 'de-DE-ConradNeural' },
    [ordered]@{ slug = 'amala'; displayName = 'Amala'; gender = 'weiblich'; voiceName = 'de-DE-AmalaNeural' },
    [ordered]@{ slug = 'gisela'; displayName = 'Gisela'; gender = 'weiblich'; voiceName = 'de-DE-GiselaNeural' },
    [ordered]@{ slug = 'katja'; displayName = 'Katja'; gender = 'weiblich'; voiceName = 'de-DE-KatjaNeural' }
)

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
$results = [Collections.Generic.List[object]]::new()
foreach ($voice in $voices) {
    $voiceDirectory = Join-Path $OutputDirectory $voice.slug
    Write-Host "Erzeuge Kardinalvergleich: $($voice.displayName)"
    & $generatorPath -OutputDirectory $voiceDirectory -VoiceName $voice.voiceName `
        -RatePercent $RatePercent -Numbers $Numbers -UseSayAsCardinal
    if ($LASTEXITCODE -ne 0) {
        throw "Kardinalvergleich für $($voice.displayName) ist fehlgeschlagen."
    }
    $manifest = Get-Content -LiteralPath (Join-Path $voiceDirectory 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.synthesisMethod -ne 'ssml-say-as-cardinal' -or @($manifest.entries).Count -ne $Numbers.Count) {
        throw "Der erzeugte Vergleich für $($voice.displayName) hat einen unvollständigen Vertrag."
    }
    $results.Add([ordered]@{
        slug = $voice.slug
        displayName = $voice.displayName
        gender = $voice.gender
        voiceName = $voice.voiceName
        previewFile = "$($voice.slug)/preview-cardinal.wav"
        previewSha256 = (Get-FileHash -LiteralPath (Join-Path $voiceDirectory 'preview-cardinal.wav') -Algorithm SHA256).Hash.ToLowerInvariant()
    })
}

$manifest = [ordered]@{
    schemaVersion = 1
    purpose = 'Experimental comparison of German GA neural voices using SSML say-as cardinal; not clinical or validated test material.'
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    synthesisMethod = 'ssml-say-as-cardinal'
    ratePercent = $RatePercent
    numbers = @($Numbers)
    referenceVoice = 'de-DE-RalfNeural (separate accepted cardinal pilot)'
    voices = @($results)
}
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'manifest.json'), (($manifest | ConvertTo-Json -Depth 6) + [Environment]::NewLine), $utf8)
$readme = @"
# Azure-Kardinalzahl-Stimmenvergleich

Sechs deutsche GA-Neuralstimmen sprechen dieselben Zahlen mittels
`say-as interpret-as="cardinal"`: $($Numbers -join ', '). Die einzelnen
Unterordner enthalten jeweils Roh-/Mess-WAVs, Manifest und eine Vorschau in
derselben Zahlenreihenfolge. Ralf bleibt als bereits abgenommene separate
Referenz erhalten und ist nicht neu erzeugt worden.

Der Vergleich entscheidet allein über subjektive Aussprache und Prosodie. Er
ist nicht klinisch validiert und kein aktives Stimuluspaket.
"@
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'README.md'), $readme, $utf8)
Write-Host "Kardinalzahl-Stimmenvergleich erzeugt: $OutputDirectory"
