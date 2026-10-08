# Pinned third-party downloads for the Windows package scripts: dot-sourced from
# build-velopack-vendored-media-tools.ps1, which build-velopack.ps1 and fetch-bundled-tool-sources.ps1
# both dot-source in turn.
#
# Every archive is fetched from its upstream URL first and checked against its pinned SHA-256. Upstream
# does not keep files forever (BtbN prunes its dated autobuild releases after a couple of weeks), so when
# the upstream download fails, the same-named file is taken from Weir's own GitHub releases, which carry a
# copy of every archive they were built with (release.yml). The same pin applies to both sources: bytes
# that do not match it are refused, wherever they came from. scripts/pinned-download.mjs does the same
# for the Node scripts.

$weirReleasesUrl = "https://api.github.com/repos/jampat000/Weir/releases?per_page=30"

function Get-WeirReleaseAssetUrl {
  # The download URL of the newest Weir release that has an asset named exactly $FileName.
  param([Parameter(Mandatory)][string]$FileName)
  $headers = @{ "User-Agent" = "weir-build"; "Accept" = "application/vnd.github+json" }
  $token = if ($env:GITHUB_TOKEN) { $env:GITHUB_TOKEN } else { $env:GH_TOKEN }
  if ($token) { $headers["Authorization"] = "Bearer $token" }
  $releases = Invoke-RestMethod -Uri $weirReleasesUrl -Headers $headers
  foreach ($release in $releases) {
    if ($release.draft) { continue }
    $asset = $release.assets | Where-Object { $_.name -ceq $FileName } | Select-Object -First 1
    if ($asset) { return $asset.browser_download_url }
  }
  throw "No Weir release has an asset named $FileName."
}

function Save-PinnedDownload {
  param(
    [Parameter(Mandatory)][string]$Url,
    [Parameter(Mandatory)][string]$OutFile,
    [Parameter(Mandatory)][string]$ExpectedSha256,
    [Parameter(Mandatory)][string]$Description
  )
  $fileName = Split-Path -Leaf $OutFile
  $source = $Url
  try {
    Invoke-WebRequest -Uri $Url -OutFile $OutFile -UseBasicParsing
  } catch {
    Write-Host "Could not download $Description from $Url ($($_.Exception.Message)); looking for $fileName on Weir's own releases."
    try {
      $source = Get-WeirReleaseAssetUrl -FileName $fileName
      Invoke-WebRequest -Uri $source -OutFile $OutFile -UseBasicParsing
    } catch {
      throw "Could not download $Description from $Url, nor from Weir's own releases: $($_.Exception.Message)"
    }
  }
  $actualSha256 = (Get-FileHash -LiteralPath $OutFile -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualSha256 -ne $ExpectedSha256) {
    throw "$Description hash mismatch. Expected $ExpectedSha256 but got $actualSha256 from $source. Upstream may have rewritten the file this pin names; re-verify before re-pinning."
  }
}
