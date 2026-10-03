# The previous release's full nupkg (`vpk pack`'s delta base) for build-velopack.ps1: dot-sourced from
# there (". $PSScriptRoot\build-velopack-previous-release.ps1"), split out (#804) so build-velopack.ps1
# stays under the project's line-count guideline (#747). Dot-sourcing runs this in the caller's own
# scope, exactly as if it were inline there.

# Fetches the full nupkg of the exact release $Version into $OutputDir, so `vpk pack` finds it there on
# its own and emits a delta package alongside the full one (Velopack.Packaging.ReleaseEntryHelper.
# GetPreviousFullRelease). The caller picks $Version: the newest published release older than the one
# being packed, pre-releases included (scripts/find-previous-release.mjs). It is asked for by tag, never
# as "the latest release", because the latest by publish date can be a pre-release or a newer version,
# neither of which is a valid delta base.
# Served from packaging/windows/vendor/previous-release (gitignored, the same as the FFmpeg/MKVToolNix
# vendor folders) when that already holds the right version, so a release only ever downloads it once.
# A release that has no full package attached, or a download that fails, leaves the pack to produce the
# full package alone: the full package is always the fallback, never a hard dependency on a prior release.
function Get-WeirPreviousReleaseFullNupkg {
  param(
    [Parameter(Mandatory)][string]$RepoUrl,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutputDir
  )
  $cacheDir = Join-Path $PSScriptRoot "vendor\previous-release"
  $packagePattern = "*-$Version-full.nupkg"
  $cached = Get-ChildItem -LiteralPath $cacheDir -Filter $packagePattern -File -ErrorAction SilentlyContinue |
    Select-Object -First 1
  if ($cached) {
    Write-Host "Using cached previous full release $($cached.Name) for delta packaging."
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    Copy-Item -LiteralPath $cached.FullName -Destination $OutputDir -Force
    return
  }

  Write-Host "Downloading the full package of release v$Version from $RepoUrl for delta packaging..."
  New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
  & gh release download "v$Version" --repo $RepoUrl --pattern $packagePattern --dir $OutputDir --clobber
  if ($LASTEXITCODE -ne 0) {
    Write-Warning "Could not download the full package of release v$Version; building the full package only."
    return
  }

  # Seeds the cache for next time from the file actually downloaded.
  $downloaded = Get-ChildItem -LiteralPath $OutputDir -Filter $packagePattern -File -ErrorAction SilentlyContinue |
    Select-Object -First 1
  if ($downloaded) {
    New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
    Copy-Item -LiteralPath $downloaded.FullName -Destination $cacheDir -Force
  }
}
