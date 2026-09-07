[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$brandDirectory = Join-Path $repoRoot 'assets\brand'
$iconDirectory = Join-Path $brandDirectory 'icons'
$fullMaster = Join-Path $brandDirectory 'devicer-mark-master.png'
$smallMaster = Join-Path $brandDirectory 'devicer-mark-small-master.png'
$magick = (Get-Command magick -ErrorAction Stop).Source

foreach ($required in $fullMaster, $smallMaster) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Brand master not found: $required"
    }
}

New-Item -ItemType Directory -Force -Path $iconDirectory | Out-Null

$logo = Join-Path $repoRoot 'branding\logo.png'
$smallLogo = Join-Path $repoRoot 'branding\logo-small.png'

& $magick $fullMaster `
    -trim +repage `
    -resize '900x900' `
    -gravity center `
    -background none `
    -extent '1024x1024' `
    -strip `
    -define 'png:color-type=6' `
    $logo
if ($LASTEXITCODE -ne 0) { throw 'Failed to build the primary logo.' }

& $magick $smallMaster `
    -trim +repage `
    -resize '880x880' `
    -gravity center `
    -background none `
    -extent '1024x1024' `
    -strip `
    -define 'png:color-type=6' `
    $smallLogo
if ($LASTEXITCODE -ne 0) { throw 'Failed to build the optical-size logo.' }

$sizes = 16, 24, 32, 48, 64, 96, 128, 256, 512, 1024
foreach ($size in $sizes) {
    $source = if ($size -le 64) { $smallLogo } else { $logo }
    $filter = if ($size -le 24) { 'box' } else { 'Lanczos' }
    $output = Join-Path $iconDirectory "devicer-$size.png"
    & $magick $source `
        -filter $filter `
        -resize "${size}x${size}" `
        -strip `
        -define 'png:color-type=6' `
        $output
    if ($LASTEXITCODE -ne 0) { throw "Failed to build the $size pixel icon." }
}

$icoInputs = 16, 24, 32, 48, 64, 96, 128, 256 | ForEach-Object {
    Join-Path $iconDirectory "devicer-$_.png"
}
& $magick @icoInputs (Join-Path $repoRoot 'branding\logo.ico')
if ($LASTEXITCODE -ne 0) { throw 'Failed to build branding\logo.ico.' }

$bannerMark = Join-Path $iconDirectory '.devicer-banner-mark.png'
$socialMark = Join-Path $iconDirectory '.devicer-social-mark.png'
& $magick $logo -resize '360x360' -strip $bannerMark
& $magick $logo -resize '440x440' -strip $socialMark

$banner = Join-Path $brandDirectory 'devicer-readme-banner.png'
& $magick `
    -size '1600x500' 'gradient:#11182d-#080d1b' `
    -fill '#152343' -draw 'circle 1460,40 1100,40' `
    $bannerMark -gravity northwest -geometry '+52+68' -composite `
    -font 'Inter-SemiBold' -fill '#F3F7FF' -pointsize 112 `
    -annotate '+452+115' 'Devicer' `
    -font 'Inter-Regular' -fill '#AEBBD2' -pointsize 37 `
    -annotate '+460+255' 'Back up first. Flash with a plan.' `
    -fill '#38D9FF' -draw 'roundrectangle 460,330 872,337 3,3' `
    -font 'Inter-Medium' -fill '#7588A8' -pointsize 21 `
    -annotate '+460+372' 'DEVICE INSIGHT   FIRMWARE   RECOVERY   CONTROL' `
    -strip `
    $banner
if ($LASTEXITCODE -ne 0) { throw 'Failed to build the README banner.' }

$social = Join-Path $brandDirectory 'devicer-social-preview.png'
& $magick `
    -size '1280x640' 'gradient:#11182d-#080d1b' `
    -fill '#152343' -draw 'circle 1160,70 820,70' `
    $socialMark -gravity northwest -geometry '+32+100' -composite `
    -font 'Inter-SemiBold' -fill '#F3F7FF' -pointsize 92 `
    -annotate '+500+185' 'Devicer' `
    -font 'Inter-Regular' -fill '#AEBBD2' -pointsize 31 `
    -annotate '+508+325' 'Back up first. Flash with a plan.' `
    -fill '#38D9FF' -draw 'roundrectangle 508,386 894,393 3,3' `
    -font 'Inter-Medium' -fill '#7588A8' -pointsize 19 `
    -annotate '+508+430' 'WINDOWS   ANDROID   LOCAL-FIRST   SAFETY GATES' `
    -strip `
    $social
if ($LASTEXITCODE -ne 0) { throw 'Failed to build the social preview.' }

Remove-Item -LiteralPath $bannerMark, $socialMark -Force
Write-Host 'Devicer brand assets built.'
