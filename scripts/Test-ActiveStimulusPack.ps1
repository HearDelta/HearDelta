[CmdletBinding()]
param(
    [string]$PackageDirectory = (Join-Path $PSScriptRoot '..\stimuli\de-DE\personal-phoneme-contrast-v1')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot 'Test-OpenAiCedarStimulusPack.ps1') `
    -PackageDirectory $PackageDirectory `
    -ExpectedGeneratorName 'Azure AI Speech' `
    -ExpectedModelRevision 'service-managed-current-2026-09-03' `
    -ExpectedVoice 'ralf'

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
