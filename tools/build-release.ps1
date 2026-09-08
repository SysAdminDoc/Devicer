#requires -Version 7
[CmdletBinding()]
param(
    [switch]$SelfContained,
    [switch]$BuildOnly,
    [switch]$PackageOnly,
    [string]$Configuration = 'Release',
    [string]$Tfm = 'net10.0-windows10.0.22621.0'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ($BuildOnly -and $PackageOnly) { throw 'Choose BuildOnly or PackageOnly, not both.' }
Push-Location $repoRoot
try {
    $dist = Join-Path $repoRoot 'dist'
    $dist = [IO.Path]::GetFullPath($dist)
    if (-not $dist.StartsWith([IO.Path]::GetFullPath($repoRoot) + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected build cleanup path.' }
    if (-not $PackageOnly -and (Test-Path -LiteralPath $dist)) {
        Remove-Item -LiteralPath $dist -Recurse -Force
    }
    New-Item -ItemType Directory -Path $dist -Force | Out-Null

    $versionNode = Select-Xml -Path 'src/Devicer.App/Devicer.App.csproj' -XPath '//Version'
    if (-not $versionNode) { throw 'Could not locate <Version> in Devicer.App.csproj.' }
    $version = $versionNode.Node.InnerText.Trim()
    if (-not $version) { throw 'The application version is empty.' }

    $publishDirectory = Join-Path $dist 'publish'
    $packageDirectory = Join-Path $dist 'package'
    if (Test-Path -LiteralPath $packageDirectory) { Remove-Item -LiteralPath $packageDirectory -Recurse -Force }
    New-Item -ItemType Directory -Path $packageDirectory | Out-Null

    $publishArgs = @(
        'publish'
        'src/Devicer.App/Devicer.App.csproj'
        '-c', $Configuration
        '-f', $Tfm
        '-r', 'win-x64'
        '-p:PublishProfile='
        '-p:DebugType=embedded'
        '-o', $publishDirectory
    )

    if ($SelfContained) {
        $publishArgs += @(
            '--self-contained', 'true'
            '-p:PublishSingleFile=true'
            '-p:IncludeNativeLibrariesForSelfExtract=true'
            '-p:EnableCompressionInSingleFile=true'
        )
    }
    else {
        $publishArgs += @('--self-contained', 'false')
    }

    if (-not $PackageOnly) {
        & dotnet @publishArgs
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
    }

    $standaloneAsset = $null
    if ($SelfContained) {
        $publishedExe = Join-Path $publishDirectory 'Devicer.App.exe'
        $standaloneAsset = Join-Path $dist "Devicer-v$version-win-x64.exe"
        if (-not $PackageOnly) {
            if (-not (Test-Path -LiteralPath $publishedExe)) { throw 'The published executable is missing.' }
            Copy-Item -LiteralPath $publishedExe -Destination $standaloneAsset
        }
        Copy-Item -LiteralPath $standaloneAsset -Destination (Join-Path $packageDirectory 'Devicer.exe')
        $zipAsset = Join-Path $dist "Devicer-v$version-win-x64.zip"
    }
    else {
        Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $packageDirectory -Recurse
        $zipAsset = Join-Path $dist "Devicer-v$version-framework-dependent-win-x64.zip"
    }

    if ($BuildOnly) { Write-Host "Built Devicer v$version. Capture and review before packaging."; return }
    if ($SelfContained) {
        & (Join-Path $PSScriptRoot 'verify-marketing.ps1') -Executable $standaloneAsset -Version $version
    }
    Copy-Item -LiteralPath 'README.md' -Destination $packageDirectory
    Copy-Item -LiteralPath 'LICENSE' -Destination $packageDirectory
    Copy-Item -LiteralPath 'assets' -Destination $packageDirectory -Recurse
    if (Test-Path -LiteralPath $zipAsset) { Remove-Item -LiteralPath $zipAsset -Force }
    Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $zipAsset -CompressionLevel Optimal
    if ($SelfContained) {
        & (Join-Path $PSScriptRoot 'verify-marketing.ps1') -Executable $standaloneAsset -Version $version -Archive $zipAsset
    }

    $assets = @($zipAsset)
    if ($standaloneAsset) { $assets = @($standaloneAsset) + $assets }

    $assetMetadata = foreach ($asset in $assets) {
        $file = Get-Item -LiteralPath $asset
        $hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
        [ordered]@{
            file = $file.Name
            bytes = $file.Length
            sha256 = $hash
        }
    }

    $checksumAsset = Join-Path $dist "Devicer-v$version-sha256.txt"
    $assetMetadata | ForEach-Object { "$($_.sha256) *$($_.file)" } | Set-Content -LiteralPath $checksumAsset -Encoding ascii

    $manifest = [ordered]@{
        product = 'Devicer'
        version = $version
        runtime = if ($SelfContained) { 'self-contained win-x64' } else { 'framework-dependent win-x64' }
        codeSigned = $false
        assets = $assetMetadata
    }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $dist "Devicer-v$version-release.json") -Encoding utf8

    foreach ($temporary in $publishDirectory, $packageDirectory) {
        if (-not [IO.Path]::GetFullPath($temporary).StartsWith($dist + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected staging cleanup path.' }
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
    }

    Write-Host "Built Devicer v$version"
    foreach ($asset in $assets) {
        Write-Host "  $([IO.Path]::GetFileName($asset))"
    }
    Write-Host "  $([IO.Path]::GetFileName($checksumAsset))"
}
finally {
    Pop-Location
}
