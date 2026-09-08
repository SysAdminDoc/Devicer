#requires -Version 7
[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$Version,
    [string]$Archive
)
$ErrorActionPreference = 'Stop'
$RepoRoot = [IO.Path]::GetFullPath($RepoRoot)
function Assert-Valid([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Get-Digest([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
$originals = [ordered]@{
    'direction-01-device-bridge.png' = 'eadeff22a44c0c8926750427db79ba389793cd3d4dae6dd4e96fe0cbd4626529'
    'direction-02-selected-device-shield.png' = '35a289e537c13c14f48bffa98641bfa3b9656f6916e8128968dd9ac6e47c5500'
    'direction-03-device-shield-light.png' = '94bb463f456ed66560fc6e410c1cc4c7535d0b6856af6b9e8799d6e30d988d89'
    'direction-04-device-shield-minimal.png' = '9446002da8ddb121f3aa445442560480f9bccbd2974af30b25bcd7049190a6e7'
}
foreach ($name in $originals.Keys) {
    Assert-Valid ((Get-Digest (Join-Path $RepoRoot "assets/brand/concepts/$name")) -eq $originals[$name]) "Original concept changed: $name"
}
Assert-Valid ((Get-Digest (Join-Path $RepoRoot 'assets/brand/devicer-selected-master.png')) -eq $originals['direction-02-selected-device-shield.png']) 'Selected master changed'
$links = 0
foreach ($document in 'README.md', 'assets/brand/concepts/README.md') {
    $path = Join-Path $RepoRoot $document
    $content = [IO.File]::ReadAllText($path)
    foreach ($match in [regex]::Matches($content, '(?:src|href)="([^"]+)"|\]\(([^)]+)\)')) {
        $link = if ($match.Groups[1].Success) { $match.Groups[1].Value } else { $match.Groups[2].Value }
        $link = $link.Split('#')[0]
        if (-not $link -or $link -match '^[a-zA-Z][a-zA-Z0-9+.-]*:') { continue }
        $target = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $path) $link))
        Assert-Valid ($target.StartsWith($RepoRoot + '\', [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $target -PathType Leaf)) "Missing local document asset: $link"
        $links++
    }
}
$readme = [IO.File]::ReadAllText((Join-Path $RepoRoot 'README.md'))
Assert-Valid ($readme.Contains("version-$Version-") -and $readme.Contains("Devicer-v$Version-win-x64.zip")) 'README version is stale'
$report = Get-Content -LiteralPath (Join-Path $RepoRoot 'assets/screenshots/capture-report.json') -Raw | ConvertFrom-Json
Assert-Valid ($report.schemaVersion -eq 2 -and $report.version -eq $Version) 'Capture version is stale'
Assert-Valid ($report.isolatedDesktop -and $report.isolatedProfile -and $report.representativeData -and $report.externalToolCommands -eq $false) 'Capture isolation evidence is incomplete'
Assert-Valid ($report.executableSha256 -eq (Get-Digest $Executable) -and $report.executableBytes -eq (Get-Item -LiteralPath $Executable).Length) 'Captures do not match this executable'
$productVersion = (Get-Item -LiteralPath $Executable).VersionInfo.ProductVersion.Split('+')[0]
Assert-Valid ($productVersion -eq $Version) 'Executable version is stale'
$names = @('01-device.png', '02-firmware.png', '03-roms.png', '04-backup.png', '05-flash-safety.png', '06-settings.png')
Assert-Valid ($report.screenshots.Count -eq 6 -and -not (Compare-Object $names @($report.screenshots.File))) 'Expected six unique product captures'
Add-Type -AssemblyName System.Drawing
foreach ($capture in $report.screenshots) {
    $path = Join-Path $RepoRoot "assets/screenshots/$($capture.File)"
    Assert-Valid ($capture.Sha256 -eq (Get-Digest $path) -and $capture.Bytes -eq (Get-Item -LiteralPath $path).Length) "Capture changed: $($capture.File)"
    $bitmap = [Drawing.Image]::FromFile($path)
    try { Assert-Valid ($bitmap.Width -eq 1600 -and $bitmap.Height -eq 1000 -and $capture.Width -eq 1600 -and $capture.Height -eq 1000) "Capture dimensions changed: $($capture.File)" }
    finally { $bitmap.Dispose() }
}
if ($Archive) {
    Add-Type -AssemblyName System.IO.Compression
    $expected = @{'Devicer.exe' = $Executable; 'README.md' = (Join-Path $RepoRoot 'README.md'); 'LICENSE' = (Join-Path $RepoRoot 'LICENSE')}
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'assets') -File -Recurse) { $expected[[IO.Path]::GetRelativePath($RepoRoot, $file.FullName).Replace('\', '/')] = $file.FullName }
    $zip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Archive))
    try {
        $entries = @($zip.Entries | Where-Object { $_.Name })
        Assert-Valid ($entries.Count -eq $expected.Count -and -not (Compare-Object @($expected.Keys) @($entries.FullName))) 'ZIP has missing or unexpected files'
        foreach ($entry in $entries) {
            $stream = $entry.Open()
            try { $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
            finally { $stream.Dispose() }
            Assert-Valid ($digest -eq (Get-Digest $expected[$entry.FullName])) "ZIP content differs: $($entry.FullName)"
        }
    } finally { $zip.Dispose() }
}
Write-Output "Verified Devicer v$Version, four originals, six captures, $links local document links$(if ($Archive) { ', and exact ZIP contents' })."
