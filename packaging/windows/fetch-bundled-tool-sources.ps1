param(
  [Parameter(Mandatory)]
  [string]$OutputDir,

  # Ignored by version control and reused between local builds, the same as the FFmpeg/MKVToolNix
  # binary vendor folders in build-velopack-vendored-media-tools.ps1. A release downloads the three
  # archives fresh: its caches are scoped to its own tag, so no other release could ever reuse them.
  [string]$CacheDir = ""
)

$ErrorActionPreference = "Stop"
# Windows PowerShell 5.1 redraws Invoke-WebRequest's progress bar for every chunk it receives, which
# makes a download many times slower than the transfer itself; nothing reads that progress here.
$ProgressPreference = "SilentlyContinue"

# Resolved here, not as the param's own default: $PSScriptRoot is unset while a script-level param
# block's default-value expressions are evaluated when the script also has a mandatory parameter (a
# PowerShell quirk, confirmed locally - Join-Path then fails with "Path" bound to an empty string).
if (-not $CacheDir) {
  $CacheDir = Join-Path $PSScriptRoot "vendor\\tool-sources"
}

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
