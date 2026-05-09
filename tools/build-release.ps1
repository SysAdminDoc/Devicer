#requires -Version 7
<#
.SYNOPSIS
    Build a Release-configuration Devicer.App, package it as a portable ZIP, emit a SHA256 sidecar.

.DESCRIPTION
    Output goes to dist/. Run from the repo root:
        pwsh tools/build-release.ps1

    The portable ZIP is framework-dependent (.NET 10 desktop runtime required on the host).
    For a self-contained single-file binary, pass -SelfContained.
#>
[CmdletBinding()]
param(
    [switch]$SelfContained,
    [string]$Configuration = 'Release',
    [string]$Tfm = 'net10.0-windows10.0.22621.0'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    Write-Host '==> Cleaning previous outputs' -ForegroundColor Cyan
    if (Test-Path dist) { Remove-Item dist -Recurse -Force }
    New-Item -ItemType Directory dist | Out-Null

    $version = (Select-Xml -Path src/Devicer.App/Devicer.App.csproj -XPath '//Version').Node.InnerText
    $tag = "v$version"
    Write-Host "==> Building Devicer $tag ($Configuration)" -ForegroundColor Cyan

    $publishArgs = @(
        'publish'
        'src/Devicer.App/Devicer.App.csproj'
        '-c', $Configuration
        '-f', $Tfm
        '-p:PublishProfile='
        '-p:DebugType=embedded'
        '-o', "dist/portable-$version"
    )
    if ($SelfContained) {
        $publishArgs += @(
            '--self-contained', 'true',
            '-r', 'win-x64',
            '-p:PublishSingleFile=true',
            '-p:IncludeNativeLibrariesForSelfExtract=true'
        )
    } else {
        $publishArgs += @(
            '--self-contained', 'false',
            '-r', 'win-x64'
        )
    }

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

    $publishDir = "dist/portable-$version"
    $zipName = "Devicer-$tag-portable-win-x64$(if ($SelfContained) {'-selfcontained'}).zip"
    $zipPath = "dist/$zipName"

    Write-Host "==> Compressing $publishDir → $zipPath" -ForegroundColor Cyan
    Compress-Archive -Path "$publishDir/*" -DestinationPath $zipPath -CompressionLevel Optimal -Force

    $sha = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLower()
    "$sha *$zipName" | Set-Content -Encoding ASCII "$zipPath.sha256"

    Write-Host '' -ForegroundColor Cyan
    Write-Host '==> Done.' -ForegroundColor Green
    Write-Host "    ZIP    : $zipPath" -ForegroundColor Green
    Write-Host "    SHA256 : $sha" -ForegroundColor Green
}
finally {
    Pop-Location
}
