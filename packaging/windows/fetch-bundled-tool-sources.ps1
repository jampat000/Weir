param(
  [Parameter(Mandatory)]
  [string]$OutputDir
)

$ErrorActionPreference = "Stop"

# #799: downloads and hash-verifies the exact source of the FFmpeg and MKVToolNix binaries the
# Windows package bundles (build-velopack-vendored-media-tools.ps1 vendors the binaries; this fetches
# their source under the same pinned-hash discipline), so release.yml can attach them to the GitHub
# Release. THIRD_PARTY_NOTICES.md points readers at "the GitHub Release" rather than a fixed URL for
# exactly this reason: the pinned versions, and so these files, change release to release.
. "$PSScriptRoot\build-velopack-vendored-media-tools.ps1"

function Get-VerifiedDownload {
  param(
    [Parameter(Mandatory)][string]$Url,
    [Parameter(Mandatory)][string]$DestinationPath,
    [Parameter(Mandatory)][string]$ExpectedSha256,
    [Parameter(Mandatory)][string]$Description
  )
  Write-Host "Downloading $Description..."
  Invoke-WebRequest -Uri $Url -OutFile $DestinationPath -UseBasicParsing
  $actualSha256 = (Get-FileHash -LiteralPath $DestinationPath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualSha256 -ne $ExpectedSha256) {
    throw "$Description hash mismatch. Expected $ExpectedSha256 but got $actualSha256 from $Url. Upstream may have rewritten the tag/commit this pin names; re-verify before re-pinning."
  }
  Write-Host "Verified $Description ($actualSha256)."
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Get-VerifiedDownload -Url $ffmpegSourceUrl `
  -DestinationPath (Join-Path $OutputDir $ffmpegSourceFileName) `
  -ExpectedSha256 $ffmpegSourceSha256 `
  -Description "FFmpeg source ($ffmpegSourceCommit)"

Get-VerifiedDownload -Url $btbnBuildScriptsUrl `
  -DestinationPath (Join-Path $OutputDir $btbnBuildScriptsFileName) `
  -ExpectedSha256 $btbnBuildScriptsSha256 `
  -Description "BtbN FFmpeg-Builds build scripts ($ffmpegReleaseTag)"

Get-VerifiedDownload -Url $mkvtoolnixSourceUrl `
  -DestinationPath (Join-Path $OutputDir $mkvtoolnixSourceFileName) `
  -ExpectedSha256 $mkvtoolnixSourceSha256 `
  -Description "MKVToolNix source ($mkvtoolnixVersion)"
