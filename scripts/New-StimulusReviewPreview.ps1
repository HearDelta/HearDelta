[CmdletBinding()]
param(
    [string]$PackageDirectory = (Join-Path $PSScriptRoot '..\stimuli\de-DE\personal-phoneme-contrast-v1'),

    [string]$OutputPath = (Join-Path $PSScriptRoot '..\diagnostics\audio\cedar-originally-problematic-comparison-2026-09-03.wav'),

    [string[]]$Words = @(
        'Bad', 'Boss', 'Butt', 'Fell', 'Grab', 'Hall', 'hin', 'hob', 'Kot', 'log',
        'Lot', 'Not', 'Ross', 'Schal', 'Tier', 'Wall', 'Weg', 'wem', 'will', 'Los'
    ),

    [ValidateRange(0.1, 3.0)]
    [double]$PauseSeconds = 0.75
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$catalogPath = Join-Path $PackageDirectory 'catalog.json'
if (-not (Test-Path -LiteralPath $catalogPath -PathType Leaf)) {
    throw "Katalog fehlt: $catalogPath"
}
$ffmpeg = Get-Command ffmpeg -ErrorAction Stop
$ffprobe = Get-Command ffprobe -ErrorAction Stop
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
$invariantCulture = [System.Globalization.CultureInfo]::InvariantCulture
$catalog = Get-Content -Raw -LiteralPath $catalogPath | ConvertFrom-Json
$catalogItems = @($catalog.lists.items)
$outputDirectory = Split-Path -Parent ([System.IO.Path]::GetFullPath($OutputPath))
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$selectedItems = foreach ($word in $Words) {
    $matches = @($catalogItems | Where-Object { [string]$_.spokenText -ieq $word })
    if ($matches.Count -ne 1) {
        throw "Prüfwort '$word' wurde $($matches.Count)-mal im Katalog gefunden."
    }
    $matches[0]
}

$temporaryId = [Guid]::NewGuid().ToString('N')
$silencePath = Join-Path $outputDirectory "_preview-silence-$temporaryId.wav"
$concatListPath = Join-Path $outputDirectory "_preview-list-$temporaryId.txt"
try {
    $pauseText = $PauseSeconds.ToString('0.000', $invariantCulture)
    & $ffmpeg.Source `
        -nostdin `
        -hide_banner `
        -loglevel error `
        -y `
        -f lavfi `
        -i 'anullsrc=r=22050:cl=mono' `
        -t $pauseText `
        -c:a pcm_s16le `
        $silencePath
    if ($LASTEXITCODE -ne 0) {
        throw 'Vorschausilenz konnte nicht erzeugt werden.'
    }

    $concatLines = [System.Collections.Generic.List[string]]::new()
    foreach ($item in $selectedItems) {
        $audioPath = Join-Path $PackageDirectory ([string]$item.audioFile).Replace('/', '\')
        if (-not (Test-Path -LiteralPath $audioPath -PathType Leaf)) {
            throw "Messdatei fehlt für '$($item.spokenText)': $audioPath"
        }
        $concatLines.Add("file '$($audioPath.Replace("'", "''"))'")
        $concatLines.Add("file '$($silencePath.Replace("'", "''"))'")
    }
    [System.IO.File]::WriteAllLines($concatListPath, $concatLines, $utf8WithoutBom)

    & $ffmpeg.Source `
        -nostdin `
        -hide_banner `
        -loglevel error `
        -y `
        -f concat `
        -safe 0 `
        -i $concatListPath `
        -c:a pcm_s16le `
        $OutputPath
    if ($LASTEXITCODE -ne 0) {
        throw 'Hörvergleich konnte nicht erzeugt werden.'
    }
}
finally {
    Remove-Item -LiteralPath $silencePath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $concatListPath -Force -ErrorAction SilentlyContinue
}

$metadataJson = & $ffprobe.Source `
    -v error `
    -select_streams a:0 `
    -show_entries stream=codec_name,sample_rate,channels,bits_per_sample,duration `
    -of json `
    $OutputPath
if ($LASTEXITCODE -ne 0) {
    throw 'Der erzeugte Hörvergleich konnte nicht geprüft werden.'
}
$stream = (($metadataJson -join "`n") | ConvertFrom-Json).streams[0]
if ($stream.codec_name -ne 'pcm_s16le' -or [int]$stream.sample_rate -ne 22050 -or
    [int]$stream.channels -ne 1 -or [int]$stream.bits_per_sample -ne 16) {
    throw 'Der erzeugte Hörvergleich hat ein unerwartetes Audioformat.'
}

[pscustomobject]@{
    OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
    Words = $selectedItems.Count
    PauseSeconds = $PauseSeconds
    DurationSeconds = [math]::Round([double]::Parse([string]$stream.duration, $invariantCulture), 3)
    Sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $OutputPath).Hash.ToLowerInvariant()
}
