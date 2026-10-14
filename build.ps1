param([string]$OutputDirectory = 'artifacts')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Push-Location $PSScriptRoot
try {
    function Invoke-Dotnet([string[]]$Arguments) {
        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) { throw "dotnet failed (exit $LASTEXITCODE): $($Arguments -join ' ')" }
    }
    Invoke-Dotnet -Arguments @('restore', 'UhOptimizer.slnx', '--configfile', 'NuGet.Config')
    Invoke-Dotnet -Arguments @('build', 'UhOptimizer.slnx', '-c', 'Release', '--no-restore')
    Invoke-Dotnet -Arguments @('run', '--project', 'tests/UhOptimizer.Tests', '-c', 'Release', '--no-build')
    $output = [IO.Path]::GetFullPath($OutputDirectory, $PSScriptRoot)
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    Invoke-Dotnet -Arguments @('publish', 'src/UhOptimizer.App/UhOptimizer.App.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=embedded', ('-p:RestoreConfigFile=' + (Join-Path $PSScriptRoot 'NuGet.Config')), '-o', $output)
    $sourceCommit = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { (& git rev-parse HEAD 2>$null) }
    if (!$sourceCommit) { $sourceCommit = 'local-uncommitted-build' }
    $sdk = & dotnet --version
    if ($LASTEXITCODE -ne 0) { throw 'Could not read .NET SDK version.' }
    [ordered]@{ app = 'Uh FiveM Optimizer'; version = '0.1.0'; sourceCommit = "$sourceCommit"; sdk = "$sdk"; target = 'win-x64'; selfContained = $true; builtUtc = [DateTimeOffset]::UtcNow.ToString('O') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'build-info.json') -Encoding utf8NoBOM
    $lines = foreach ($name in @('UhFiveMOptimizer.exe', 'build-info.json')) {
        $hash = (Get-FileHash -LiteralPath (Join-Path $output $name) -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $name"
    }
    $lines | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
    Write-Output "Published to $output"
} finally { Pop-Location }
