# Vendored FFmpeg and MKVToolNix for build-velopack.ps1: dot-sourced from there
# (". $PSScriptRoot\build-velopack-vendored-media-tools.ps1"), split out (#747) so build-velopack.ps1
# stays under the project's line-count guideline. Dot-sourcing runs this in the caller's own scope, so
# $PSScriptRoot here is still packaging/windows (the dot-sourcing script's own folder), and the
# variables and functions below are usable from build-velopack.ps1 exactly as if this were inline.
#
# Both tools follow the same download / verify / cache shape: a pinned version and checksum, a stamp
# file recording what is currently vendored, and a re-hash of the extracted exe(s) on every run
# (including a cache hit) so a stale or tampered vendor folder is caught without trusting the stamp's
# own say-so about what it contains.

# Ignored by version control and reused between builds: Ensure-WindowsFfmpegRuntime downloads again
# only when the vendored copy doesn't match the pin below (CI caches this folder too).
#
# Pinned the same way #548 pins MKVToolNix below: a specific, immutable BtbN release rather than
# the `latest` tag. BtbN republishes `latest` from git master continuously, so it is a moving
# target with no stable checksum authority of its own — its checksums.sha256 is published inside
# that same mutable release, which proves a download wasn't corrupted in transit, not who built it
# or that it is the build Weir was tested against.
#
# To bump: browse https://github.com/BtbN/FFmpeg-Builds/releases, pick a dated
# autobuild-YYYY-MM-DD-HH-MM tag (or a versioned "release" build such as n9.0.2 if BtbN has cut
# one), and find the win64 LGPL (non-shared) zip in its assets — e.g. with
# `gh release view <tag> -R BtbN/FFmpeg-Builds --json assets`. Update $ffmpegVersion,
# $ffmpegReleaseTag and $ffmpegArchiveName together, then get $ffmpegArchiveSha256 by downloading
# that zip to a scratch folder (never the repo) and hashing it yourself — don't just copy the
# number from the release's own checksums.sha256, since that is the thing being pinned against.
# Extract the zip and hash ffmpeg.exe/ffprobe.exe for $ffmpegExeSha256/$ffprobeExeSha256. Finally
# delete packaging/windows/vendor/ffmpeg and re-run this script once to pick up and verify the
# new build end to end.
$ffmpegVersion = "n9.0.2 (BtbN autobuild-2026-09-23-14-55)"
$ffmpegReleaseTag = "autobuild-2026-09-23-14-55"
$ffmpegVendorDir = Join-Path $PSScriptRoot "vendor\\ffmpeg"
$ffmpegArchiveName = "ffmpeg-n9.0.2-3-ga5923073bf-win64-lgpl-9.0.zip"
$ffmpegArchiveUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/$ffmpegReleaseTag/$ffmpegArchiveName"
$ffmpegArchiveSha256 = "90e05f6093e72d44ed931413444b5a50b18cf6683674bea53b89b9f0532603fd"
$ffmpegExeSha256 = "957331f732598e71938c4e6d083e58bf3b222525445482799cece09b54814411"
$ffprobeExeSha256 = "3400f29503df724150c05ac162149e913fce682a603ba8101fd6f3df9dd67c24"
# #548: MKVToolNix, for the mkvmerge writer (Weir.Infrastructure.Media.MkvmergeRemuxWriter). Vendored,
# cached and pinned exactly like ffmpeg above, against MKVToolNix's own immutable per-version release
# directories rather than BtbN's. Bumping means editing both lines below together — the checksum is
# the one upstream publishes at
# https://mkvtoolnix.download/windows/releases/<version>/mkvtoolnix-64-bit-<version>.zip.sha256
# (also listed in that directory's sha256sums.txt). Weir uses only mkvmerge's long-stable CLI surface
# (`-o`, `--identification-format json`, per-track selection — see Weir.Core.Media.MkvmergeCommands),
# so any current stable release serves.
$mkvtoolnixVersion = "102.0"
$mkvtoolnixArchiveSha256 = "c02e918900f6d945d9307b426237e456378b79a200589ac6928de39064409a44"
$mkvtoolnixVendorDir = Join-Path $PSScriptRoot "vendor\\mkvtoolnix"
$mkvtoolnixArchiveName = "mkvtoolnix-64-bit-$mkvtoolnixVersion.zip"
$mkvtoolnixArchiveUrl = "https://mkvtoolnix.download/windows/releases/$mkvtoolnixVersion/$mkvtoolnixArchiveName"

function Assert-WindowsFfmpegExeHashes {
  # Re-hashing the extracted exes — both right after download and again on every cache hit — is
  # deliberately redundant with the archive-hash check: it catches the vendor folder being
  # repopulated by something other than this function (a stale or tampered CI cache entry, a
  # half-written previous run) without trusting the stamp file's own say-so about what it
  # contains. Verified against the pinned constants above, not against whatever the stamp records.
  param(
    [Parameter(Mandatory)][string]$FfmpegExePath,
    [Parameter(Mandatory)][string]$FfprobeExePath
  )
  $actualFfmpegSha256 = (Get-FileHash -LiteralPath $FfmpegExePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualFfmpegSha256 -ne $ffmpegExeSha256) {
    throw "ffmpeg.exe hash mismatch at $FfmpegExePath. Expected $ffmpegExeSha256 but got $actualFfmpegSha256. Delete $ffmpegVendorDir and re-run, or re-pin the hash if $ffmpegVersion was deliberately bumped."
  }
  $actualFfprobeSha256 = (Get-FileHash -LiteralPath $FfprobeExePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualFfprobeSha256 -ne $ffprobeExeSha256) {
    throw "ffprobe.exe hash mismatch at $FfprobeExePath. Expected $ffprobeExeSha256 but got $actualFfprobeSha256. Delete $ffmpegVendorDir and re-run, or re-pin the hash if $ffmpegVersion was deliberately bumped."
  }
}

function Ensure-WindowsFfmpegRuntime {
  $ffmpegExe = Join-Path $ffmpegVendorDir "ffmpeg.exe"
  $ffprobeExe = Join-Path $ffmpegVendorDir "ffprobe.exe"
  $stampPath = Join-Path $ffmpegVendorDir ".ffmpeg-archive.sha256"

  if ((Test-Path -LiteralPath $ffmpegExe) -and
      (Test-Path -LiteralPath $ffprobeExe) -and
      (Test-Path -LiteralPath $stampPath)) {
    $vendoredSha256 = (Get-Content -LiteralPath $stampPath -Raw).Trim().ToLowerInvariant()
    if ($vendoredSha256 -eq $ffmpegArchiveSha256) {
      Assert-WindowsFfmpegExeHashes -FfmpegExePath $ffmpegExe -FfprobeExePath $ffprobeExe
      Write-Host "Vendored FFmpeg $ffmpegVersion already matches the pinned hashes; skipping download."
      return
    }
    Write-Host "Vendored FFmpeg archive stamp is stale (have $vendoredSha256, want $ffmpegArchiveSha256); refreshing."
  }

  $downloadRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("weir-ffmpeg-" + [System.Guid]::NewGuid().ToString("N"))
  $archivePath = Join-Path $downloadRoot $ffmpegArchiveName
  $extractRoot = Join-Path $downloadRoot "extract"
  try {
    New-Item -ItemType Directory -Path $downloadRoot | Out-Null
    New-Item -ItemType Directory -Path $extractRoot | Out-Null
    Write-Host "Downloading Windows FFmpeg runtime ($ffmpegVersion)..."
    Invoke-WebRequest -Uri $ffmpegArchiveUrl -OutFile $archivePath -UseBasicParsing
    $actualSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualSha256 -ne $ffmpegArchiveSha256) {
      throw "Downloaded FFmpeg archive hash mismatch. Expected $ffmpegArchiveSha256 but got $actualSha256."
    }
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractRoot -Force
    $binDir = Get-ChildItem -Path $extractRoot -Recurse -Directory |
      Where-Object {
        (Test-Path (Join-Path $_.FullName "ffmpeg.exe")) -and
        (Test-Path (Join-Path $_.FullName "ffprobe.exe"))
      } |
      Select-Object -First 1
    if (-not $binDir) {
      throw "Downloaded FFmpeg archive did not contain ffmpeg.exe and ffprobe.exe."
    }
    Assert-WindowsFfmpegExeHashes `
      -FfmpegExePath (Join-Path $binDir.FullName "ffmpeg.exe") `
      -FfprobeExePath (Join-Path $binDir.FullName "ffprobe.exe")
    if (Test-Path $ffmpegVendorDir) {
      Remove-Item -LiteralPath $ffmpegVendorDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $ffmpegVendorDir | Out-Null
    foreach ($name in @("ffmpeg.exe", "ffprobe.exe")) {
      $src = Join-Path $binDir.FullName $name
      Copy-Item -LiteralPath $src -Destination (Join-Path $ffmpegVendorDir $name) -Force
    }
    Set-Content -LiteralPath (Join-Path $ffmpegVendorDir ".ffmpeg-archive.sha256") -Value $ffmpegArchiveSha256 -Encoding ascii
  } finally {
    if (Test-Path $downloadRoot) {
      Remove-Item -LiteralPath $downloadRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
  }
}

function Ensure-WindowsMkvtoolnixRuntime {
  # #548: the same download / verify / cache shape as Ensure-WindowsFfmpegRuntime, against a pinned
  # version and checksum instead of an upstream checksums file. Only mkvmerge.exe is vendored: the
  # portable archive is ~85 MB because it carries the Qt GUI, mkvinfo, mkvextract, mkvpropedit, the
  # docs and 40-odd locales, none of which Weir ever runs. MKVToolNix's Windows CLI binaries are
  # statically linked — no sibling DLLs, no data directory — verified by copying mkvmerge.exe alone
  # out of an install and running `--version` and an `--identification-format json` identify from the
  # copy, both of which succeeded. That keeps the package's growth to the ~23 MB of mkvmerge itself
  # rather than the ~120 MB the whole archive would expand to.
  $mkvmergeExe = Join-Path $mkvtoolnixVendorDir "mkvmerge.exe"
  $stampPath = Join-Path $mkvtoolnixVendorDir ".mkvtoolnix-archive.sha256"

  if ((Test-Path -LiteralPath $mkvmergeExe) -and (Test-Path -LiteralPath $stampPath)) {
    $vendoredSha256 = (Get-Content -LiteralPath $stampPath -Raw).Trim().ToLowerInvariant()
    if ($vendoredSha256 -eq $mkvtoolnixArchiveSha256) {
      Write-Host "Vendored MKVToolNix already matches the pin ($mkvtoolnixVersion, $mkvtoolnixArchiveSha256); skipping download."
      return
    }
    Write-Host "Vendored MKVToolNix is stale (have $vendoredSha256, want $mkvtoolnixArchiveSha256); refreshing."
  }

  $downloadRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("weir-mkvtoolnix-" + [System.Guid]::NewGuid().ToString("N"))
  $archivePath = Join-Path $downloadRoot $mkvtoolnixArchiveName
  $extractRoot = Join-Path $downloadRoot "extract"
  try {
    New-Item -ItemType Directory -Path $downloadRoot | Out-Null
    New-Item -ItemType Directory -Path $extractRoot | Out-Null
    Write-Host "Downloading MKVToolNix $mkvtoolnixVersion..."
    Invoke-WebRequest -Uri $mkvtoolnixArchiveUrl -OutFile $archivePath -UseBasicParsing
    $actualSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualSha256 -ne $mkvtoolnixArchiveSha256) {
      throw "Downloaded MKVToolNix archive hash mismatch. Expected $mkvtoolnixArchiveSha256 but got $actualSha256."
    }
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractRoot -Force
    # The archive nests everything under a "mkvtoolnix\" folder, but searching for the directory that
    # actually holds mkvmerge.exe (rather than hard-coding that name) means a future layout change
    # fails loudly on the throw below instead of silently shipping a package without the tool.
    $binDir = Get-ChildItem -Path $extractRoot -Recurse -Directory |
      Where-Object { Test-Path (Join-Path $_.FullName "mkvmerge.exe") } |
      Select-Object -First 1
    if (-not $binDir) {
      throw "Downloaded MKVToolNix archive did not contain mkvmerge.exe."
    }
    if (Test-Path $mkvtoolnixVendorDir) {
      Remove-Item -LiteralPath $mkvtoolnixVendorDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $mkvtoolnixVendorDir | Out-Null
    Copy-Item -LiteralPath (Join-Path $binDir.FullName "mkvmerge.exe") -Destination $mkvmergeExe -Force
    Set-Content -LiteralPath $stampPath -Value $mkvtoolnixArchiveSha256 -Encoding ascii
  } finally {
    if (Test-Path $downloadRoot) {
      Remove-Item -LiteralPath $downloadRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
  }
}
