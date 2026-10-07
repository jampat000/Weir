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
# The shared build (ffmpeg.exe/ffprobe.exe linked against av*.dll instead of each embedding its
# own copy of every library) rather than BtbN's static build, because Weir vendors both exes: the
# static zip is ~163 MB compressed with each exe carrying its own ~130 MB of statically-linked
# libraries, while the shared zip is ~73 MB compressed with the exes under 1 MB combined and the six
# av*.dll files (avcodec/avdevice/avfilter/avformat/avutil/swresample/swscale) shared between them.
# Windows always searches an exe's own directory for a DLL first regardless of PATH (the "safe DLL
# search order"), so ffmpeg.exe and ffprobe.exe find their av*.dll siblings without any code change —
# MediaToolLocations/MediaToolResolver only ever check that ffmpeg.exe and ffprobe.exe exist in the
# candidate directory (apps/server/src/Weir.Infrastructure/Media/MediaToolResolver.cs), never their
# internal linkage. ffplay.exe ships in the same zip but is never copied out: Weir has no use for it.
#
# To bump: browse https://github.com/BtbN/FFmpeg-Builds/releases, pick a dated
# autobuild-YYYY-MM-DD-HH-MM tag (or a versioned "release" build such as n9.0.2 if BtbN has cut
# one), and find the win64 LGPL **shared** zip in its assets — e.g. with
# `gh release view <tag> -R BtbN/FFmpeg-Builds --json assets`. Update $ffmpegVersion,
# $ffmpegReleaseTag and $ffmpegArchiveName together, then get $ffmpegArchiveSha256 by downloading
# that zip to a scratch folder (never the repo) and hashing it yourself — don't just copy the
# number from the release's own checksums.sha256, since that is the thing being pinned against.
# Extract the zip and hash ffmpeg.exe, ffprobe.exe and every av*.dll under bin\ for
# $ffmpegExeSha256/$ffprobeExeSha256/$ffmpegSharedLibrarySha256 (a name -> hash map, one entry per
# library — the versioned filenames such as avcodec-63.dll change across ffmpeg releases). Finally
# delete packaging/windows/vendor/ffmpeg and re-run this script once to pick up and verify the
# new build end to end.
$ffmpegVersion = "n9.0.2 (BtbN autobuild-2026-09-23-14-55, shared)"
$ffmpegReleaseTag = "autobuild-2026-09-23-14-55"
$ffmpegVendorDir = Join-Path $PSScriptRoot "vendor\\ffmpeg"
$ffmpegArchiveName = "ffmpeg-n9.0.2-3-ga5923073bf-win64-lgpl-shared-9.0.zip"
$ffmpegArchiveUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/$ffmpegReleaseTag/$ffmpegArchiveName"
$ffmpegArchiveSha256 = "1db36dc94e379e3a7e974e995e278da05d46c14ebb6deb7da11bc8ad1a4a3e3e"
$ffmpegExeSha256 = "6044d10e9eacdd9b8c71d4f900edb1e26fc9825b0792049ab4d157912830fc9f"
$ffprobeExeSha256 = "b1d740b988e14c0522a4bf31a3e1310cdbb4f2920b9d83d34b5e47f1a7ff682c"
# The libraries ffmpeg.exe/ffprobe.exe load from their own directory. Names carry the BtbN build's
# so-version suffix (bumps only when that library's ABI changes upstream), verified the same way as
# the exes above: re-hashed on every run, including a cache hit, never trusted from the stamp file.
$ffmpegSharedLibrarySha256 = [ordered]@{
  "avcodec-63.dll"    = "3097eeaff204934e1d9b5535465a86ec7aca89cbd7c7d8a26703a6f044c505da"
  "avdevice-63.dll"   = "1b17db579dd5ccb343b77677ef1745fd06ed44ef1410f26a0fa5f82386d2ba6a"
  "avfilter-12.dll"   = "5d17ac69edb24c3bb4f151a27bf35c306b6d26b45b870691b35bf7a018e334e8"
  "avformat-63.dll"   = "43c9984ebf7d5e6ab72e9609571d51d4da119d9827bde906ff74313ee836b906"
  "avutil-61.dll"     = "cd9fba6890d5b4cd992f3abe2b1c059df18b8d29f93dfa47b66f66720b7bb0cf"
  "swresample-7.dll"  = "7ead1f572f8c0db2106cc74e6c829fcd15753f58b6baab96df4c9efeef77f4a4"
  "swscale-10.dll"    = "420f146d39c7be6c45a1b51c39249800c0ccb0702b70bf9e5c45984f81afc756"
}
# The Linux CI jobs (contract suite, Docker smoke, release candidate) use the static LGPL linux64 build
# of the same BtbN release (same FFmpeg commit as the Windows build above), so no job takes ffmpeg from a
# package mirror. scripts/install-ffmpeg-linux.mjs reads these two lines; bump them with the Windows ones
# and hash the tar.xz yourself in the same way.
$ffmpegLinuxArchiveName = "ffmpeg-n9.0.2-3-ga5923073bf-linux64-lgpl-9.0.tar.xz"
$ffmpegLinuxArchiveSha256 = "a7ee2de0d9d462f4bb7cd4e23255338089ac34bf8286b7e68ba8768363ac9de3"
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

# AGPL/LGPL/GPL source-availability pins, downloaded and hash-verified by
# fetch-bundled-tool-sources.ps1 and attached to each GitHub Release, so the exact source behind the
# bundled binaries above travels with the release rather than only a link to the projects (the
# binary pins above prove what was bundled; these prove where its source is). Each pin identifies the
# precise commit or version the corresponding binary pin was built from — update all three together
# whenever $ffmpegArchiveName/$ffmpegReleaseTag/$mkvtoolnixVersion above are bumped.
#
# BtbN's build string ("n9.0.2 (BtbN autobuild-2026-09-23-14-55, shared)") names the FFmpeg git
# describe output "n9.0.2-3-ga5923073bf": 3 commits after the n9.0.2 tag, at commit a5923073bf. That
# exact commit, not the n9.0.2 tag, is what BtbN actually compiled, so it is what must be linked.
$ffmpegSourceCommit = "a5923073bf"
$ffmpegSourceUrl = "https://github.com/FFmpeg/FFmpeg/archive/$ffmpegSourceCommit.tar.gz"
$ffmpegSourceSha256 = "15c067b10c9a71fb3db9107284d75c53dd637c949d2ef7edacc3b0789f656499"
$ffmpegSourceFileName = "ffmpeg-source-$ffmpegSourceCommit.tar.gz"

# BtbN/FFmpeg-Builds is both the release repo and the build-scripts repo: the release tag is a git
# ref pointing at the exact commit its assets were built from, so a source archive of that tag is
# the matching build-scripts commit, not just "whatever master happens to be now".
$btbnBuildScriptsUrl = "https://github.com/BtbN/FFmpeg-Builds/archive/refs/tags/$ffmpegReleaseTag.tar.gz"
$btbnBuildScriptsSha256 = "3ca96f4c3fb03531506f0b6c7b1a8786845354f6df02e3ad08f33040f49d44ef"
$btbnBuildScriptsFileName = "ffmpeg-build-scripts-$ffmpegReleaseTag.tar.gz"

$mkvtoolnixSourceUrl = "https://mkvtoolnix.download/sources/mkvtoolnix-$mkvtoolnixVersion.tar.xz"
$mkvtoolnixSourceSha256 = "9f0a810f17c7df8adb9064a3a41d5784399be412d19704cf080745ad7d45da30"
$mkvtoolnixSourceFileName = "mkvtoolnix-source-$mkvtoolnixVersion.tar.xz"

function Assert-WindowsFfmpegExeHashes {
  # Re-hashing the extracted exes and shared libraries — both right after download and again on
  # every cache hit — is deliberately redundant with the archive-hash check: it catches the vendor
  # folder being repopulated by something other than this function (a stale or tampered CI cache
  # entry, a half-written previous run) without trusting the stamp file's own say-so about what it
  # contains. Verified against the pinned constants above, not against whatever the stamp records.
  param(
    [Parameter(Mandatory)][string]$FfmpegExePath,
    [Parameter(Mandatory)][string]$FfprobeExePath,
    [Parameter(Mandatory)][string]$BinDir
  )
  $actualFfmpegSha256 = (Get-FileHash -LiteralPath $FfmpegExePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualFfmpegSha256 -ne $ffmpegExeSha256) {
    throw "ffmpeg.exe hash mismatch at $FfmpegExePath. Expected $ffmpegExeSha256 but got $actualFfmpegSha256. Delete $ffmpegVendorDir and re-run, or re-pin the hash if $ffmpegVersion was deliberately bumped."
  }
  $actualFfprobeSha256 = (Get-FileHash -LiteralPath $FfprobeExePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualFfprobeSha256 -ne $ffprobeExeSha256) {
    throw "ffprobe.exe hash mismatch at $FfprobeExePath. Expected $ffprobeExeSha256 but got $actualFfprobeSha256. Delete $ffmpegVendorDir and re-run, or re-pin the hash if $ffmpegVersion was deliberately bumped."
  }
  foreach ($libraryName in $ffmpegSharedLibrarySha256.Keys) {
    $libraryPath = Join-Path $BinDir $libraryName
    if (-not (Test-Path -LiteralPath $libraryPath)) {
      throw "Expected shared library $libraryName was not found in $BinDir. The BtbN shared build layout may have changed; re-pin `$ffmpegSharedLibrarySha256 in build-velopack-vendored-media-tools.ps1."
    }
    $actualLibrarySha256 = (Get-FileHash -LiteralPath $libraryPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $expectedLibrarySha256 = $ffmpegSharedLibrarySha256[$libraryName]
    if ($actualLibrarySha256 -ne $expectedLibrarySha256) {
      throw "$libraryName hash mismatch at $libraryPath. Expected $expectedLibrarySha256 but got $actualLibrarySha256. Delete $ffmpegVendorDir and re-run, or re-pin the hash if $ffmpegVersion was deliberately bumped."
    }
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
      Assert-WindowsFfmpegExeHashes -FfmpegExePath $ffmpegExe -FfprobeExePath $ffprobeExe -BinDir $ffmpegVendorDir
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
      -FfprobeExePath (Join-Path $binDir.FullName "ffprobe.exe") `
      -BinDir $binDir.FullName
    if (Test-Path $ffmpegVendorDir) {
      Remove-Item -LiteralPath $ffmpegVendorDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $ffmpegVendorDir | Out-Null
    $vendoredFileNames = @("ffmpeg.exe", "ffprobe.exe") + @($ffmpegSharedLibrarySha256.Keys)
    foreach ($name in $vendoredFileNames) {
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
