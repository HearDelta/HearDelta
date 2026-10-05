[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\diagnostics\audio\azure-cardinal-number-christoph-full-2026-09-08'),

    [switch]$ForceApi
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$generator = Join-Path $PSScriptRoot 'New-AzureCardinalNumberPilot.ps1'
& $generator -OutputDirectory $OutputDirectory -VoiceName 'de-DE-ChristophNeural' -VoiceDisplayName 'Christoph' `
    -RatePercent -30 -Numbers (100..999) -UseSayAsCardinal -SkipPreview -ForceApi:$ForceApi

$manifestPath = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$actualValues = @($manifest.entries | ForEach-Object { [int]$_.numericValue } | Sort-Object)
if ($actualValues.Count -ne 900 -or (@(Compare-Object -ReferenceObject (100..999) -DifferenceObject $actualValues)).Count -ne 0) {
    throw 'Das Christoph-Material enthält nicht genau die Kardinalzahlen 100 bis 999.'
}
if ($manifest.voice -ne 'de-DE-ChristophNeural' -or $manifest.synthesisMethod -ne 'ssml-say-as-cardinal') {
    throw 'Das Manifest bestätigt nicht die festgelegte Christoph-Kardinalsynthese.'
}

Write-Host "Christoph-Kardinalzahl-Material vollständig erzeugt: $OutputDirectory"
