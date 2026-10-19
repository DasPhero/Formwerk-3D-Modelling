<#
.SYNOPSIS
  Builds Formwerk, runs the tests and publishes the self-contained win-x64 executable.

.EXAMPLE
  .\scripts\build.ps1              # build + test + publish
  .\scripts\build.ps1 -SkipTests   # build + publish only
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# Pick up a freshly installed SDK in shells that were opened before the install.
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

function Invoke-Step([string]$Title, [scriptblock]$Action) {
    Write-Host "== $Title" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "$Title failed (exit code $LASTEXITCODE)" }
}

# Everything needed to run the program, ready to copy to another computer (no .NET install needed there).
$publishDir = Join-Path $root 'binary'

Invoke-Step 'Build solution' { dotnet build Formwerk.sln -c $Configuration --nologo -v minimal }

if (-not $SkipTests) {
    # One test project at a time (-maxcpucount:1): the speed tests measure wall-clock time and need a quiet machine.
    Invoke-Step 'Run tests' { dotnet test Formwerk.sln -c $Configuration --no-build --nologo -v minimal -maxcpucount:1 }
}

$running = Get-Process Formwerk -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($publishDir, [StringComparison]::OrdinalIgnoreCase) }
if ($running) { throw "Formwerk is running from '$publishDir'. Close it first (the folder is replaced by every build)." }
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
Invoke-Step 'Publish self-contained win-x64' {
    dotnet publish src/Formwerk.App/Formwerk.App.csproj -c $Configuration -r win-x64 --self-contained true -o $publishDir -p:DebugType=None -p:DebugSymbols=false --nologo -v minimal
}

$exe = Join-Path $publishDir 'Formwerk.exe'
if (-not (Test-Path $exe)) { throw "Expected executable not found: $exe" }
Write-Host "OK: $exe" -ForegroundColor Green
