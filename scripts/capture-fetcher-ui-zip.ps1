# Build (if needed), install Playwright Chromium if needed, then zip all Fetcher tab screenshots to artifacts/fetcher-ui-tabs.zip
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$py = Join-Path $repoRoot "apps\backend\.venv\Scripts\python.exe"
$script = Join-Path $repoRoot "scripts\capture_fetcher_ui_zip.py"

if (-not (Test-Path $py)) {
    Write-Error "Missing $py - create the backend venv and pip install -e . first."
}
if (-not (Test-Path $script)) {
    Write-Error "Missing $script"
}

$webDir = Join-Path $repoRoot "apps\web"
Write-Host "Building apps/web (so preview matches current UI)..." -ForegroundColor DarkGray
Push-Location $webDir
try {
    & npm run build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
} finally {
    Pop-Location
}

Write-Host "Installing playwright package (if needed)..." -ForegroundColor DarkGray
& $py -m pip install "playwright>=1.49.0" -q
Write-Host "Installing Chromium for Playwright (if needed)..." -ForegroundColor DarkGray
& $py -m playwright install chromium

Set-Location $repoRoot
& $py $script
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$zip = Join-Path $repoRoot "artifacts\fetcher-ui-tabs.zip"
Write-Host ""
Write-Host "Done. Grab:" -ForegroundColor Green
Write-Host ('  ' + $zip)
