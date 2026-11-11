# QuickShot GitHub Publisher Script
$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

$gitPath = "$env:LOCALAPPDATA\Programs\Git\cmd\git.exe"
if (-not (Test-Path $gitPath)) {
    $gitPath = (Get-Command git -ErrorAction SilentlyContinue).Source
}
if (-not (Test-Path $gitPath)) {
    Write-Host "Git executable not found." -ForegroundColor Red
    exit 1
}

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  Pushing QuickShot to GitHub Repository  " -ForegroundColor Cyan
Write-Host "  Repository: https://github.com/moshclusive/quickshot" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

Write-Host "`n1. Verifying branch and remote..." -ForegroundColor Yellow
& $gitPath branch -M main
& $gitPath remote set-url origin https://github.com/moshclusive/quickshot.git

Write-Host "`n2. Pushing main branch..." -ForegroundColor Yellow
& $gitPath push -u origin main

Write-Host "`n3. Pushing release tag v1.0.0..." -ForegroundColor Yellow
& $gitPath push origin v1.0.0

Write-Host "`n==========================================" -ForegroundColor Green
Write-Host "  Successfully Pushed to GitHub! 🎉       " -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
Write-Host "Next Step: Go to https://github.com/moshclusive/quickshot/releases/new" -ForegroundColor Yellow
Write-Host "Select tag 'v1.0.0' and attach 'Setup\QuickShot-Setup.exe' (15.78 MB)." -ForegroundColor Yellow

