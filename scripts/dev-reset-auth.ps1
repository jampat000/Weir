# Clear local users + sessions so /setup (first admin) works again. See scripts/dev_reset_auth.py.
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$script = Join-Path $repoRoot "scripts\dev_reset_auth.py"
$venvPy = Join-Path $repoRoot "apps\backend\.venv\Scripts\python.exe"
if (Test-Path $venvPy) {
    & $venvPy $script @args
} else {
    py -3 $script @args
}
exit $LASTEXITCODE
