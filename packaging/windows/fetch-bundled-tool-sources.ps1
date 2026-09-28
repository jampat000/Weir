param(
  [Parameter(Mandatory)]
  [string]$OutputDir,

  # Ignored by version control and reused between builds, the same as the FFmpeg/MKVToolNix binary
  # vendor folders in build-velopack-vendored-media-tools.ps1: release.yml caches this directory
  # (actions/cache, keyed on a hash of that file, so a pin bump always invalidates it), so a release
  # only ever downloads these three archives once per pin, not once per release.
  [string]$CacheDir = (Join-Path $PSScriptRoot "vendor\\tool-sources")
)

$ErrorActionPreference = "Stop"

# Downloads and hash-verifies the exact source of the FFmpeg and MKVToolNix binaries the
# Windows package bundles (build-velopack-vendored-media-tools.ps1 vendors the binaries; this fetches
# their source under the same pinned-hash discipline), so release.yml can attach them to the GitHub
# Release. THIRD_PARTY_NOTICES.md points readers at "the GitHub Release" rather than a fixed URL for
# exactly this reason: the pinned versions, and so these files, change release to release.
. "$PSScriptRoot\build-velopack-vendored-media-tools.ps1"

function Get-CachedVerifiedDownload {
  param(
    [Parameter(Mandatory)][string]$Url,
    [Parameter(Mandatory)][string]$FileName,
    [Parameter(Mandatory)][string]$ExpectedSha256,
    [Parameter(Mandatory)][string]$Description
  )
  $destinationPath = Join-Path $OutputDir $FileName
  $cachedPath = Join-Path $CacheDir $FileName
  if (Test-Path -LiteralPath $cachedPath) {
    $cachedSha256 = (Get-FileHash -LiteralPath $cachedPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($cachedSha256 -eq $ExpectedSha256) {
      Write-Host "Using cached $Description ($cachedSha256)."
      Copy-Item -LiteralPath $cachedPath -Destination $destinationPath -Force
      return
    }
    Write-Host "Cached $Description is stale (have $cachedSha256, want $ExpectedSha256); refreshing."
  }
  Write-Host "Downloading $Description..."
  Invoke-WebRequest -Uri $Url -OutFile $destinationPath -UseBasicParsing
  $actualSha256 = (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualSha256 -ne $ExpectedSha256) {
    throw "$Description hash mismatch. Expected $ExpectedSha256 but got $actualSha256 from $Url. Upstream may have rewritten the tag/commit this pin names; re-verify before re-pinning."
  }
  Write-Host "Verified $Description ($actualSha256)."
  New-Item -ItemType Directory -Path $CacheDir -Force | Out-Null
  Copy-Item -LiteralPath $destinationPath -Destination $cachedPath -Force
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Get-CachedVerifiedDownload -Url $ffmpegSourceUrl `
  -FileName $ffmpegSourceFileName `
  -ExpectedSha256 $ffmpegSourceSha256 `
  -Description "FFmpeg source ($ffmpegSourceCommit)"

Get-CachedVerifiedDownload -Url $btbnBuildScriptsUrl `
  -FileName $btbnBuildScriptsFileName `
  -ExpectedSha256 $btbnBuildScriptsSha256 `
  -Description "BtbN FFmpeg-Builds build scripts ($ffmpegReleaseTag)"

Get-CachedVerifiedDownload -Url $mkvtoolnixSourceUrl `
  -FileName $mkvtoolnixSourceFileName `
  -ExpectedSha256 $mkvtoolnixSourceSha256 `
  -Description "MKVToolNix source ($mkvtoolnixVersion)"
