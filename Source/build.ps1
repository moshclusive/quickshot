$ErrorActionPreference = "Stop"
$sourceDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$setupDir = Join-Path (Split-Path -Parent $sourceDir) "Setup"

Write-Host "=== 1. Building QuickShot (Release x64) ===" -ForegroundColor Cyan
dotnet build (Join-Path $sourceDir "QuickShot.csproj") -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$x86Dir = Join-Path $sourceDir "bin\Release\net48\x86"
if (Test-Path $x86Dir) { Remove-Item $x86Dir -Recurse -Force }

Write-Host "=== 2. Building One-File Installer ===" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $setupDir | Out-Null
$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) {
    throw "Inno Setup 6 not found at $iscc"
}
& $iscc (Join-Path $sourceDir "installer.iss")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exeOut = Join-Path $setupDir "QuickShot-Setup.exe"
$sizeMB = [Math]::Round((Get-Item $exeOut).Length / 1MB, 2)
Write-Host "=== Done! Setup built at: $exeOut ($sizeMB MB) ===" -ForegroundColor Green

