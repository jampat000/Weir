# The previous release's full nupkg (`vpk pack`'s delta base) for build-velopack.ps1: dot-sourced from
# there (". $PSScriptRoot\build-velopack-previous-release.ps1"), split out (#804) so build-velopack.ps1
# stays under the project's line-count guideline (#747). Dot-sourcing runs this in the caller's own
# scope, exactly as if it were inline there.

# Fetches the previous release's full nupkg into $OutputDir, so `vpk pack` finds it there on its own
# and emits a delta package alongside the full one (Velopack.Packaging.ReleaseEntryHelper.
# GetPreviousFullRelease). Served from packaging/windows/vendor/previous-release (gitignored, the same
# as the FFmpeg/MKVToolNix vendor folders; release.yml's own actions/cache sits in front of it, keyed
# on $Version) when that already holds the right version, so a release only ever downloads it once.
# When no previous release exists yet (a brand-new repo, or every existing release predates this
# channel), `vpk download github` logs a warning and returns without error, and pack simply produces
# the full package as before: the full package is always the fallback, never a hard dependency on
# there being a prior release to diff against.
function Get-WeirPreviousReleaseFullNupkg {
  param(
    [Parameter(Mandatory)][string]$RepoUrl,
    [string]$Version,
    [Parameter(Mandatory)][string]$OutputDir,
    [Parameter(Mandatory)][string]$VpkExePath
  )
  $cacheDir = Join-Path $PSScriptRoot "vendor\\previous-release"
  $cached = $null
  if ($Version) {
    $cached = Get-ChildItem -LiteralPath $cacheDir -Filter "*-$Version-full.nupkg" -File -ErrorAction SilentlyContinue |
      Select-Object -First 1
  }
  if ($cached) {
    Write-Host "Using cached previous full release $($cached.Name) for delta packaging."
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    Copy-Item -LiteralPath $cached.FullName -Destination $OutputDir -Force
    return
  }

  Write-Host "Downloading the previous full release from $RepoUrl for delta packaging..."
  $downloadArgs = @("download", "github", "--repoUrl", $RepoUrl, "--outputDir", $OutputDir)
  if ($env:GITHUB_TOKEN) {
    $downloadArgs += @("--token", $env:GITHUB_TOKEN)
  }
  & $VpkExePath @downloadArgs
  if ($LASTEXITCODE -ne 0) {
    throw ("Command failed with exit code {0}: {1} {2}" -f $LASTEXITCODE, $VpkExePath, ($downloadArgs -join " "))
  }

  # Seeds the cache for next time from whatever was actually downloaded (the file on disk, not
  # $Version, is the source of truth for its own name) so a release.yml run for the next version, with
  # the same previous version, can skip this download entirely.
  $downloaded = Get-ChildItem -LiteralPath $OutputDir -Filter "*-full.nupkg" -File -ErrorAction SilentlyContinue |
    Select-Object -First 1
  if ($downloaded) {
    New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
    Copy-Item -LiteralPath $downloaded.FullName -Destination $cacheDir -Force
  }
}
