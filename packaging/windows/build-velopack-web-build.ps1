# The web app's background build for build-velopack.ps1: dot-sourced from there
# (". $PSScriptRoot\build-velopack-web-build.ps1"), split out (#804) so build-velopack.ps1 stays under
# the project's line-count guideline (#747). Dot-sourcing runs this in the caller's own scope, exactly
# as if it were inline there.
#
# Runs as a background job (Start-Job): nothing needs the built web app until the server publish smoke
# further down build-velopack.ps1, so this overlaps FFmpeg/MKVToolNix vendoring and the .NET server
# publish instead of blocking them. It writes to a private temp directory and only ever touches
# $WebDir\dist, never $distRoot, so nothing here races build-velopack.ps1's own "Clean dist".

# Starts the background build; call Wait-WeirWebBuild on the returned job before anything reads
# $WebDir\dist. Returns $null without starting anything when the caller does not want a web build.
function Start-WeirWebBuild {
  param(
    [Parameter(Mandatory)][string]$RepoRoot,
    [Parameter(Mandatory)][string]$WebDir,
    [switch]$Skip
  )
  if ($Skip) {
    return $null
  }
  return Start-Job -Name "weir-web-build" -ArgumentList $RepoRoot, $WebDir -ScriptBlock {
    param([string]$RepoRoot, [string]$WebDir)
    # Every cmdlet below passes -ErrorAction Stop explicitly rather than setting
    # $ErrorActionPreference = "Stop" for the whole job: inside a background job, that preference turns
    # even a successful native command's stderr output into a terminating error (npm's own deprecation
    # notices did exactly this), in a way this script's own top-level scope does not. Invoke-NativeInJob
    # below is this job's equivalent of build-velopack.ps1's own Invoke-Native, and exists only because
    # a job runs in its own process with no access to that function.
    function Invoke-NativeInJob {
      param([Parameter(Mandatory)][string]$FilePath, [string[]]$ArgumentList)
      # Merging stderr into the success stream with `2>&1` and stringifying every line is what keeps
      # PowerShell from recording it as an ErrorRecord: an ErrorRecord this job's own error stream
      # holds, however non-terminating, becomes terminating for the caller the moment Receive-Job
      # replays it into build-velopack.ps1's own $ErrorActionPreference = "Stop" scope, whatever exit
      # code the command actually returned. $LASTEXITCODE (checked by the caller) is what decides pass
      # or fail here.
      & $FilePath @ArgumentList 2>&1 | ForEach-Object { $_.ToString() }
    }

    $webBuildRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("weir-web-build-" + [System.Guid]::NewGuid().ToString("N"))
    $webBuildWebDir = Join-Path $webBuildRoot "apps\\web"
    $webBuildScriptsDir = Join-Path $webBuildRoot "scripts"
    try {
      New-Item -ItemType Directory -Path $webBuildWebDir -ErrorAction Stop | Out-Null
      New-Item -ItemType Directory -Path $webBuildScriptsDir -ErrorAction Stop | Out-Null
      Copy-Item -LiteralPath (Join-Path $RepoRoot "scripts\\dev-ports.json") -Destination (Join-Path $webBuildScriptsDir "dev-ports.json") -Force -ErrorAction Stop
      $copyArgs = @(
        $WebDir,
        $webBuildWebDir,
        "/MIR",
        "/XD",
        "node_modules",
        "dist",
        ".vite",
        "tmp",
        "/XF",
        "*.log"
      )
      Invoke-NativeInJob -FilePath robocopy -ArgumentList $copyArgs
      if ($LASTEXITCODE -gt 7) {
        throw ("Command failed with exit code {0}: robocopy {1}" -f $LASTEXITCODE, ($copyArgs -join " "))
      }

      Push-Location $webBuildWebDir
      Invoke-NativeInJob -FilePath npm.cmd -ArgumentList @("ci")
      if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $($LASTEXITCODE): npm ci"
      }
      Invoke-NativeInJob -FilePath npm.cmd -ArgumentList @("run", "build")
      if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $($LASTEXITCODE): npm run build"
      }

      $sourceDist = Join-Path $webBuildWebDir "dist"
      $targetDist = Join-Path $WebDir "dist"
      if (Test-Path $targetDist) {
        Remove-Item -LiteralPath $targetDist -Recurse -Force -ErrorAction Stop
      }
      Copy-Item -LiteralPath $sourceDist -Destination $targetDist -Recurse -Force -ErrorAction Stop
    } finally {
      if ((Get-Location).Path -eq $webBuildRoot) {
        Pop-Location
      }
      if (Test-Path $webBuildRoot) {
        Remove-Item -LiteralPath $webBuildRoot -Recurse -Force -ErrorAction SilentlyContinue
      }
    }
  }
}

# Blocks until the background build finishes (a no-op when $Job is $null, i.e. Start-WeirWebBuild was
# called with -Skip) and throws with the job's own error when it failed. Not `Receive-Job -ErrorAction
# Stop`: see Invoke-NativeInJob above for why that would fail the build on a harmless native warning.
function Wait-WeirWebBuild {
  param([System.Management.Automation.Job]$Job)
  if (-not $Job) {
    return
  }
  Receive-Job -Job $Job -Wait | Out-Host
  if ($Job.State -eq "Failed") {
    throw "Background web build failed: $($Job.ChildJobs[0].JobStateInfo.Reason.Message)"
  }
  Remove-Job -Job $Job
}
