[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PiperPath,

    [Parameter(Mandatory)]
    [string]$ModelPath,

    [string]$CatalogSourcePath = (Join-Path $PSScriptRoot '..\stimuli\de-DE\personal-relative-v1\catalog-source.csv'),

    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\stimuli\de-DE\personal-relative-v1')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedPiperSha256 = '96F3DA3811151580073E40BB4DD20EB0FB8115F5F5F76E2FB54282B3EDFA5C1F'
$expectedModelSha256 = '9DF1C43C61149EF9B39E618E2B861FBE41E1FCEA9390B2DAC62E8761573EA4F1'
$expectedConfigSha256 = '6DE734444E4C3F9E33B7EBE2746DBC19B71E85F613E79C65ACF623200B99A76A'
$modelConfigPath = "$ModelPath.json"

foreach ($requiredPath in @($PiperPath, $ModelPath, $modelConfigPath, $CatalogSourcePath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Erforderliche Datei fehlt: $requiredPath"
    }
}

function Assert-Sha256 {
    param([string]$Path, [string]$Expected)
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash
    if ($actual -ne $Expected) {
        throw "SHA-256 stimmt für '$Path' nicht: erwartet $Expected, gefunden $actual."
    }
}

Assert-Sha256 -Path $PiperPath -Expected $expectedPiperSha256
Assert-Sha256 -Path $ModelPath -Expected $expectedModelSha256
Assert-Sha256 -Path $modelConfigPath -Expected $expectedConfigSha256

$rows = @(Import-Csv -LiteralPath $CatalogSourcePath -Delimiter ';')
if ($rows.Count -eq 0) {
    throw 'Der Stimulusquellkatalog ist leer.'
}

$duplicateIds = $rows | Group-Object stimulus_id | Where-Object Count -gt 1
if ($duplicateIds) {
    throw "Doppelte Stimulus-IDs: $($duplicateIds.Name -join ', ')"
}

$audioDirectory = Join-Path $OutputDirectory 'audio'
New-Item -ItemType Directory -Path $audioDirectory -Force | Out-Null

$catalogLists = foreach ($group in ($rows | Group-Object list_id | Sort-Object Name)) {
    $materials = @($group.Group.material | Select-Object -Unique)
    if ($materials.Count -ne 1) {
        throw "Liste '$($group.Name)' enthält mehrere Materialarten."
    }
    if ($group.Count -ne 20) {
        throw "Liste '$($group.Name)' enthält $($group.Count) statt 20 Stimuli."
    }

    [ordered]@{
        id = $group.Name
        material = $materials[0]
        items = @($group.Group | ForEach-Object {
            [ordered]@{
                id = $_.stimulus_id
                spokenText = $_.spoken_text
                canonicalResponse = $_.canonical_response
                audioFile = "audio/$($_.stimulus_id).wav"
            }
        })
    }
}

$catalog = [ordered]@{
    schemaVersion = 1
    id = 'de-DE-personal-relative-v1'
    version = '1.0.0'
    language = 'de-DE'
    title = 'Deutsches persönliches Vergleichsmaterial v1'
    license = 'CC0-1.0'
    clinicallyValidated = $false
    itemsPerList = 20
    lists = @($catalogLists)
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$catalogPath = Join-Path $OutputDirectory 'catalog.json'
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText(
    $catalogPath,
    (($catalog | ConvertTo-Json -Depth 8) + [Environment]::NewLine),
    $utf8WithoutBom)

$audioEntries = foreach ($row in $rows) {
    $finalPath = Join-Path $audioDirectory "$($row.stimulus_id).wav"
    $temporaryPath = "$finalPath.tmp.wav"
    try {
        $row.spoken_text |
            & $PiperPath --model $ModelPath --config $modelConfigPath --output_file $temporaryPath `
                --noise_scale 0 --noise_w 0 --length_scale 1 --sentence_silence 0.2 --quiet |
            Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Piper ist für '$($row.stimulus_id)' mit Exitcode $LASTEXITCODE fehlgeschlagen."
        }
        if ((Get-Item -LiteralPath $temporaryPath).Length -le 44) {
            throw "Piper hat für '$($row.stimulus_id)' keine gültige WAV-Datei erzeugt."
        }
        Move-Item -LiteralPath $temporaryPath -Destination $finalPath -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }

    [ordered]@{
        stimulusId = $row.stimulus_id
        audioFile = "audio/$($row.stimulus_id).wav"
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $finalPath).Hash.ToLowerInvariant()
        byteLength = (Get-Item -LiteralPath $finalPath).Length
    }
}

$audioIndex = [ordered]@{
    schemaVersion = 1
    catalogId = $catalog.id
    catalogVersion = $catalog.version
    catalogSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $catalogPath).Hash.ToLowerInvariant()
    audioFormat = [ordered]@{
        encoding = 'PCM_SIGNED'
        sampleRate = 22050
        bitsPerSample = 16
        channels = 1
    }
    generator = [ordered]@{
        name = 'Piper'
        version = '2023.11.14-2'
        executableSha256 = $expectedPiperSha256.ToLowerInvariant()
        modelRepository = 'rhasspy/piper-voices'
        modelRevision = '39ab474be869e9181350af6a65e4953eef67aaa0'
        modelName = 'de_DE-thorsten-high'
        modelSha256 = $expectedModelSha256.ToLowerInvariant()
        configSha256 = $expectedConfigSha256.ToLowerInvariant()
        modelLicense = 'MIT'
        datasetLicense = 'CC0-1.0'
        syntheticVoice = $true
        noiseScale = 0
        phonemeWidthNoise = 0
        lengthScale = 1
        sentenceSilenceSeconds = 0.2
    }
    entries = @($audioEntries)
}

$audioIndexPath = Join-Path $OutputDirectory 'audio-index.json'
[System.IO.File]::WriteAllText(
    $audioIndexPath,
    (($audioIndex | ConvertTo-Json -Depth 8) + [Environment]::NewLine),
    $utf8WithoutBom)

Write-Host "Stimuluspaket erzeugt: $($rows.Count) Mono-WAVs in '$audioDirectory'."
Write-Host "Katalog: $catalogPath"
Write-Host "Audioindex: $audioIndexPath"
