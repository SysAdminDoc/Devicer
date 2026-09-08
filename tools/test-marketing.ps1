#requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$Version
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$validator = Join-Path $PSScriptRoot 'verify-marketing.ps1'
$Executable = [IO.Path]::GetFullPath($Executable)
& $validator -Executable $Executable -Version $Version
$scratch = Join-Path ([IO.Path]::GetTempPath()) ("DevicerMarketingTests-" + [Guid]::NewGuid().ToString('N'))
$cases = @(
    @{ Name = 'missing guide image'; Expected = 'Missing local document asset'; Change = {
        param($root)
        Remove-Item -LiteralPath (Join-Path $root 'assets/brand/devicer-readme-banner.png')
    } },
    @{ Name = 'modified original concept'; Expected = 'Original concept changed'; Change = {
        param($root)
        [IO.File]::AppendAllText((Join-Path $root 'assets/brand/concepts/direction-01-device-bridge.png'), 'invalid')
    } },
    @{ Name = 'stale capture version'; Expected = 'Capture version is stale'; Change = {
        param($root)
        $path = Join-Path $root 'assets/screenshots/capture-report.json'
        $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $report.version = '0.0.0'
        $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path
    } },
    @{ Name = 'wrong executable hash'; Expected = 'Captures do not match this executable'; Change = {
        param($root)
        $path = Join-Path $root 'assets/screenshots/capture-report.json'
        $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $report.executableSha256 = '0' * 64
        $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path
    } },
    @{ Name = 'changed screenshot'; Expected = 'Capture changed'; Change = {
        param($root)
        [IO.File]::AppendAllText((Join-Path $root 'assets/screenshots/01-device.png'), 'invalid')
    } },
    @{ Name = 'duplicate screenshot record'; Expected = 'Expected six unique product captures'; Change = {
        param($root)
        $path = Join-Path $root 'assets/screenshots/capture-report.json'
        $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $report.screenshots[5] = $report.screenshots[0]
        $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path
    } },
    @{ Name = 'incomplete ZIP'; Expected = 'ZIP has missing or unexpected files'; Change = {
        param($root)
        Compress-Archive -LiteralPath (Join-Path $root 'README.md') -DestinationPath (Join-Path $root 'incomplete.zip')
    }; Archive = $true }
)
try {
    New-Item -ItemType Directory -Path $scratch | Out-Null
    for ($index = 0; $index -lt $cases.Count; $index++) {
        $case = $cases[$index]
        $root = Join-Path $scratch $index
        New-Item -ItemType Directory -Path $root | Out-Null
        foreach ($item in 'README.md', 'LICENSE', 'assets') { Copy-Item -LiteralPath (Join-Path $repo $item) -Destination $root -Recurse }
        & $case.Change $root
        $arguments = @{ RepoRoot = $root; Executable = $Executable; Version = $Version }
        if ($case.Archive) { $arguments.Archive = Join-Path $root 'incomplete.zip' }
        $rejected = $false
        try { & $validator @arguments }
        catch {
            if (-not $_.Exception.Message.Contains($case.Expected)) { throw }
            $rejected = $true
        }
        if (-not $rejected) { throw "Invalid fixture was accepted: $($case.Name)" }
        Write-Output "Rejected $($case.Name)."
    }
} finally {
    $resolved = [IO.Path]::GetFullPath($scratch)
    if (-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase) -or -not ([IO.Path]::GetFileName($resolved).StartsWith('DevicerMarketingTests-'))) { throw 'Unexpected test cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
Write-Output "Passed baseline verification and $($cases.Count) rejection fixtures."
