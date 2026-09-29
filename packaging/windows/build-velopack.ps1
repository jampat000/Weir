param(
  [switch]$SkipWebBuild,
  [switch]$SkipDotnetPublish,
  [switch]$SkipSmoke,
  # When set, the previous release's full nupkg is fetched into $velopackOut before `vpk
  # pack` runs, so vpk builds a delta package against it as well as the full one. Left empty for
  # local builds and the PR-triggered windows-package-smoke job (ci-packaging.yml): neither ships
  # anything, and a delta with no consumer is just a slower, network-dependent build. release.yml
  # passes the repo URL so every real release gets a delta wherever a base release exists.
  [string]$PreviousReleaseRepoUrl = "",

  # The previous release's bare version (e.g. "3.2.10"), when known. Lets the fetch above be served
  # from release.yml's own actions/cache instead of a `vpk download` on every release: the cache is
  # keyed on this exact version, so it can never serve a stale package (packaging/windows/vendor/
  # previous-release, gitignored the same as the FFmpeg/MKVToolNix vendor folders). Left empty when
  # the previous version is not known up front (local builds; the very first release).
  [string]$PreviousReleaseVersion = ""
)

$ErrorActionPreference = "Stop"
# Windows PowerShell 5.1 redraws Invoke-WebRequest's progress bar for every chunk it receives, which
# makes a download many times slower than the transfer itself; nothing reads that progress here.
$ProgressPreference = "SilentlyContinue"

# Builds the Weir Windows package (Velopack): the web app, the .NET server
# (apps/server/src/Weir.Host, self-contained single-file win-x64), the tray app (apps/tray/Weir.Tray)
# and a checksum-verified ffmpeg and mkvmerge, packed as packId Weir with Weir.exe (the tray) as the
# main exe.
# Output: dist\windows\releases\{Weir-win-Setup.exe, Weir-win-Portable.zip, *.nupkg, RELEASES, ...}.
#
# Layout of the pack directory (dist\windows\pack), which is what gets installed:
#   Weir.exe                          the tray app; it starts and watches the server
#   server\WeirServer.exe             the server (Weir.Host publishes as Weir.exe, renamed so it does
#                                     not collide with the tray's own Weir.exe; the tray's
#                                     FindServerExeDirectory looks for this name)
#   server\web-dist\                  the built web app (the tray sets WEIR_WEB_DIST to it)
#   server\bin\ffmpeg\{ffmpeg,ffprobe}.exe + av*.dll
#                                     found by the server's MediaToolResolver as <app>\bin\ffmpeg;
#                                     the shared BtbN build, so the av*.dll libraries sit beside the
#                                     exes instead of being statically linked into each
#   server\bin\mkvtoolnix\mkvmerge.exe
#                                     found by MediaToolResolver.ResolveMkvmerge as
#                                     <app>\bin\mkvtoolnix (#548)
#
# scripts/smoke-windows-package.ps1 then proves the assembled server works end to end.
#
# Phase timing (Start-BuildPhase/Stop-BuildPhase/Write-BuildPhaseSummary), the vendored FFmpeg/MKVToolNix
# provisioning (Ensure-WindowsFfmpegRuntime/Ensure-WindowsMkvtoolnixRuntime), the background web build
# (Start-WeirWebBuild/Wait-WeirWebBuild) and the previous release's delta base
# (Get-WeirPreviousReleaseFullNupkg) are split into sibling files and dot-sourced below, so this script
# stays under the project's line-count guideline (#747). Dot-sourcing runs them in this script's own
# scope, exactly as if they were inline here.
. "$PSScriptRoot\build-velopack-phase-timing.ps1"
. "$PSScriptRoot\build-velopack-vendored-media-tools.ps1"
. "$PSScriptRoot\build-velopack-web-build.ps1"
. "$PSScriptRoot\build-velopack-previous-release.ps1"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\\..")).Path
$serverProjectDir = Join-Path $repoRoot "apps\\server\\src\\Weir.Host"
$webDir = Join-Path $repoRoot "apps\\web"
$trayDir = Join-Path $repoRoot "apps\\tray\\Weir.Tray"
$distRoot = Join-Path $repoRoot "dist\\windows"
$velopackOut = Join-Path $distRoot "releases"
$trayPublishDir = Join-Path $distRoot "tray-publish"
$serverPublishDir = Join-Path $distRoot "server-publish"

function Invoke-Native {
  param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,

    [Parameter()]
    [string[]]$ArgumentList
  )

  & $FilePath @ArgumentList
  if ($LASTEXITCODE -ne 0) {
    throw ("Command failed with exit code {0}: {1} {2}" -f $LASTEXITCODE, $FilePath, ($ArgumentList -join " "))
  }
}

# ── Resolve the version to build ──
# #804: there is no per-release version checked into the tree any more. WEIR_BUILD_VERSION (release.yml
# sets it from the tag) is the real product version and is trusted as-is; every local or PR build that
# leaves it unset falls back to WeirVersion in apps/server/Directory.Build.props, which is a fixed
# placeholder that never ships (scripts/check-release-version.mjs validates the tag itself before this
# script ever runs).
$propsPath = Join-Path $repoRoot "apps\\server\\Directory.Build.props"
$propsMatch = [regex]::Match((Get-Content -LiteralPath $propsPath -Raw), '<WeirVersion>([^<]+)</WeirVersion>')
if (-not $propsMatch.Success) {
  throw "WeirVersion was not found in $propsPath."
}
$buildVersion = if ($env:WEIR_BUILD_VERSION) {
  $env:WEIR_BUILD_VERSION
} else {
  $propsMatch.Groups[1].Value.Trim()
}
if ($buildVersion.StartsWith("v")) {
  $buildVersion = $buildVersion.Substring(1)
}

# ── Web build (started in the background; nothing needs its output until the server publish smoke
#    below, so it overlaps FFmpeg/MKVToolNix vendoring and the .NET server publish instead of blocking
#    them; see build-velopack-web-build.ps1) ──
# Timed with its own stopwatch, not Start-BuildPhase: it overlaps the phases that follow, so folding
# its time into their sequential total would overstate how long the build actually took end to end.
$webBuildStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$webBuildJob = Start-WeirWebBuild -RepoRoot $repoRoot -WebDir $webDir -Skip:$SkipWebBuild

# ── Clean dist ──
if ($SkipDotnetPublish) {
  if (-not (Test-Path -LiteralPath $trayPublishDir) -or -not (Test-Path -LiteralPath $serverPublishDir)) {
    throw "-SkipDotnetPublish requires existing publish output at $trayPublishDir and $serverPublishDir."
  }
  Get-ChildItem -LiteralPath $distRoot -Force |
    Where-Object { $_.Name -notin @("tray-publish", "server-publish") } |
    Remove-Item -Recurse -Force
} elseif (Test-Path $distRoot) {
  Remove-Item -LiteralPath $distRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null

# ── FFmpeg ──
Start-BuildPhase "FFmpeg download + vendor"
Ensure-WindowsFfmpegRuntime

# ── MKVToolNix (#548) ──
Start-BuildPhase "MKVToolNix download + vendor"
Ensure-WindowsMkvtoolnixRuntime

# ── .NET server publish (self-contained single-file, via the checked-in win-x64 publish
#    profile — apps/server/src/Weir.Host/Properties/PublishProfiles/win-x64.pubxml — the same
#    way apps/server/README.md documents, NOT by repeating --self-contained/-p:PublishSingleFile
#    etc. as separate command-line switches). That distinction matters: a command-line
#    -p:PublishSingleFile=true is a *global* MSBuild property, applied to every project in the
#    build graph including Weir.Infrastructure, which makes its build fail with error IL3000
#    ("Assembly.Location always returns an empty string in a single-file app") on the
#    Assembly.Location check in Runtime/SystemServices.cs (DetectInstallType) — code that reads
#    that empty string ON PURPOSE, to detect single-file mode. Weir.Host.csproj's own
#    RID-conditioned PropertyGroup (and the .pubxml profiles built on top of it) set the same
#    properties, but scoped to the Weir.Host project only, which is what the analyzer actually
#    expects; publishing this way never triggers IL3000 (see apps/server/README.md), so it stays a
#    profile rather than explicit -p: flags.
Start-BuildPhase ".NET server publish"
if (-not $SkipDotnetPublish) {
  Write-Host "Publishing .NET server..."
  if (Test-Path $serverPublishDir) {
    Remove-Item -LiteralPath $serverPublishDir -Recurse -Force
  }
  # The committed packages.lock.json files record the plain restore CI checks with --locked-mode. A win-x64
  # publish restores more (the runtime's packages and the single-file tooling), so it keeps its own lock file
  # under each project's obj folder instead of rewriting the committed one.
  Invoke-Native -FilePath dotnet -ArgumentList @(
    "publish", $serverProjectDir,
    "-p:PublishProfile=win-x64",
    "-p:Version=$buildVersion",
    "-p:PublishDir=$serverPublishDir\",
    "-p:NuGetLockFilePath=obj/publish.packages.lock.json"
  )
}
$publishedServerExe = Join-Path $serverPublishDir "Weir.exe"
if (-not (Test-Path -LiteralPath $publishedServerExe)) {
  throw "Expected published .NET server executable was not found: $publishedServerExe"
}
$serverVersion = (& $publishedServerExe --version).Trim()
if ($serverVersion -ne $buildVersion) {
  throw "Published Weir.exe (server) reports version '$serverVersion' but expected build version is '$buildVersion'."
}

# ── Join the background web build: the smoke below is the first thing that needs its output ──
Wait-WeirWebBuild -Job $webBuildJob
$webBuildStopwatch.Stop()
Write-Host ("--- Web build (background): {0}s, overlapped with the phases above ---" -f [math]::Round($webBuildStopwatch.Elapsed.TotalSeconds, 1))
$webDistDir = Join-Path $webDir "dist"
if (-not (Test-Path -LiteralPath (Join-Path $webDistDir "index.html"))) {
  throw "Expected a built web app at $webDistDir (index.html missing). Re-run without -SkipWebBuild."
}

# ── Smoke: the raw published server exe, before packing (temp WEIR_HOME + the built web dist) ──
if (-not $SkipSmoke) {
  Start-BuildPhase "Server publish smoke"
  $smokeHome = Join-Path ([System.IO.Path]::GetTempPath()) ("weir-dotnet-server-smoke-" + [System.Guid]::NewGuid().ToString("N"))
  New-Item -ItemType Directory -Path $smokeHome | Out-Null
  $smokePort = 8799
  $smokeProc = $null
  $oldHome = $env:WEIR_HOME
  $oldWebDist = $env:WEIR_WEB_DIST
  $oldSecret = $env:WEIR_SESSION_SECRET
  $oldCookieSecure = $env:WEIR_SESSION_COOKIE_SECURE
  try {
    $env:WEIR_HOME = $smokeHome
    $env:WEIR_WEB_DIST = $webDistDir
    $env:WEIR_SESSION_SECRET = "package-build-smoke-session-secret-32chars-min"
    $env:WEIR_SESSION_COOKIE_SECURE = "false"
    $smokeProc = Start-Process -FilePath $publishedServerExe `
      -ArgumentList @("--port", [string]$smokePort) `
      -WorkingDirectory $serverPublishDir `
      -RedirectStandardOutput (Join-Path $smokeHome "server.stdout.log") `
      -RedirectStandardError (Join-Path $smokeHome "server.stderr.log") `
      -WindowStyle Hidden `
      -PassThru
    $healthUrl = "http://127.0.0.1:$smokePort/health"
    $deadline = (Get-Date).AddSeconds(30)
    $healthy = $false
    do {
      if ($smokeProc.HasExited) {
        throw "Published .NET server exited early with code $($smokeProc.ExitCode) during the publish smoke test."
      }
      try {
        $response = Invoke-RestMethod -Uri $healthUrl -Method Get -TimeoutSec 2
        Write-Host ("Smoke /health responded: {0}" -f ($response | ConvertTo-Json -Compress))
        $healthy = $true
        break
      } catch {
        Start-Sleep -Milliseconds 250
      }
    } while ((Get-Date) -lt $deadline)
    if (-not $healthy) {
      throw "Published .NET server did not answer $healthUrl within 30s."
    }
    Write-Host "Server publish smoke passed: $healthUrl"
  } finally {
    if ($smokeProc -and -not $smokeProc.HasExited) {
      Stop-Process -Id $smokeProc.Id -Force -ErrorAction SilentlyContinue
    }
    if ($null -ne $oldHome) { $env:WEIR_HOME = $oldHome } else { Remove-Item Env:\WEIR_HOME -ErrorAction SilentlyContinue }
    if ($null -ne $oldWebDist) { $env:WEIR_WEB_DIST = $oldWebDist } else { Remove-Item Env:\WEIR_WEB_DIST -ErrorAction SilentlyContinue }
    if ($null -ne $oldSecret) { $env:WEIR_SESSION_SECRET = $oldSecret } else { Remove-Item Env:\WEIR_SESSION_SECRET -ErrorAction SilentlyContinue }
    if ($null -ne $oldCookieSecure) { $env:WEIR_SESSION_COOKIE_SECURE = $oldCookieSecure } else { Remove-Item Env:\WEIR_SESSION_COOKIE_SECURE -ErrorAction SilentlyContinue }
    Remove-Item -LiteralPath $smokeHome -Recurse -Force -ErrorAction SilentlyContinue
  }
}

# ── .NET tray app publish ──
Start-BuildPhase ".NET tray publish"
if (-not $SkipDotnetPublish) {
  Write-Host "Publishing .NET tray app..."
  Invoke-Native -FilePath dotnet -ArgumentList @(
    "publish", $trayDir,
    "-c", "Release",
    "--self-contained",
    "-r", "win-x64",
    "-o", $trayPublishDir,
    "-p:Version=$buildVersion"
  )
}

# ── Assemble Velopack pack directory ──
Start-BuildPhase "Assemble pack dir"
$packDir = Join-Path $distRoot "pack"
if (Test-Path $packDir) {
  Remove-Item -LiteralPath $packDir -Recurse -Force
}
New-Item -ItemType Directory -Path $packDir | Out-Null

Write-Host "Assembling Velopack pack directory..."
Copy-Item -Path (Join-Path $trayPublishDir "*") -Destination $packDir -Recurse -Force

# The tray app looks for "server\WeirServer.exe" (apps/tray/Weir.Tray/ServerHost.cs, FindServerExeDirectory). Weir.Host's own
# AssemblyName is "Weir" (it would collide with the tray's own Weir.exe at the pack root if copied under
# its published name), so the publish output is renamed on the way in.
$serverDestDir = Join-Path $packDir "server"
New-Item -ItemType Directory -Path $serverDestDir | Out-Null
Copy-Item -Path (Join-Path $serverPublishDir "*") -Destination $serverDestDir -Recurse -Force
Move-Item -LiteralPath (Join-Path $serverDestDir "Weir.exe") -Destination (Join-Path $serverDestDir "WeirServer.exe") -Force
$serverPdb = Join-Path $serverDestDir "Weir.pdb"
if (Test-Path -LiteralPath $serverPdb) {
  # Not expected (Directory.Build.props sets DebugType=embedded), but if a future change
  # reintroduces a companion .pdb, keep its name aligned with the renamed executable.
  Move-Item -LiteralPath $serverPdb -Destination (Join-Path $serverDestDir "WeirServer.pdb") -Force
}

# The tray points WEIR_WEB_DIST at "server\web-dist" (apps/tray/Weir.Tray/ServerHost.cs, PrepareEnvironment).
Copy-Item -Path $webDistDir -Destination (Join-Path $serverDestDir "web-dist") -Recurse -Force

# Matches the <packaged-app-dir>\bin\ffmpeg candidate in
# apps/server/src/Weir.Core/Media/MediaToolLocations.cs. ffmpeg.exe/ffprobe.exe are the shared BtbN
# build, so their av*.dll siblings ($ffmpegSharedLibrarySha256's keys) come along too — Windows
# resolves them from the exe's own directory before ever consulting PATH.
$serverFfmpegDir = Join-Path $serverDestDir "bin\\ffmpeg"
New-Item -ItemType Directory -Path $serverFfmpegDir -Force | Out-Null
$ffmpegVendoredFileNames = @("ffmpeg.exe", "ffprobe.exe") + @($ffmpegSharedLibrarySha256.Keys)
foreach ($ffmpegFileName in $ffmpegVendoredFileNames) {
  Copy-Item -Path (Join-Path $ffmpegVendorDir $ffmpegFileName) -Destination $serverFfmpegDir -Force
}

# #548: matches the <packaged-app-dir>\bin\mkvtoolnix candidate in
# MediaToolLocations.MkvtoolnixCandidateDirectories (MkvtoolnixBundleDirectory). Without it
# MediaToolResolver.ResolveMkvmerge finds nothing in a packaged install, and the per-library writer
# setting's "best" default (RemuxWriterChoice) quietly falls back to ffmpeg for every write.
$serverMkvtoolnixDir = Join-Path $serverDestDir "bin\\mkvtoolnix"
New-Item -ItemType Directory -Path $serverMkvtoolnixDir -Force | Out-Null
Copy-Item -Path (Join-Path $mkvtoolnixVendorDir "mkvmerge.exe") -Destination $serverMkvtoolnixDir -Force

# ── vpk pack (packId Weir, mainExe Weir.exe: the install identity every release keeps) ──
Start-BuildPhase "vpk install"
# The vpk CLI must match the Velopack library the tray was built with, pinned in the tray's central package file.
$trayPackagesPath = Join-Path $repoRoot "apps\\tray\\Directory.Packages.props"
[xml]$trayPackages = Get-Content -LiteralPath $trayPackagesPath -Raw
$velopackPackage = @($trayPackages.Project.ItemGroup.PackageVersion) |
  Where-Object { $_.Include -eq "Velopack" } |
  Select-Object -First 1
$velopackCliVersion = [string]$velopackPackage.Version
if (-not $velopackCliVersion) {
  throw "Velopack package version was not found in $trayPackagesPath."
}

# Every global tool, then filtered here: `dotnet tool list -g vpk` exits 1 when vpk is not installed yet, which is
# exactly the case this has to handle.
$vpkListLine = @(Invoke-Native -FilePath dotnet -ArgumentList @("tool", "list", "-g")) |
  Where-Object { $_ -match "^\s*vpk\s+" } |
  Select-Object -First 1
$installedVpkVersion = if ($vpkListLine) { ($vpkListLine.Trim() -split "\s+")[1] } else { $null }
if (-not $installedVpkVersion) {
  Write-Host "Installing Velopack CLI $velopackCliVersion..."
  Invoke-Native -FilePath dotnet -ArgumentList @("tool", "install", "-g", "vpk", "--version", $velopackCliVersion)
} elseif ($installedVpkVersion -ne $velopackCliVersion) {
  Write-Host "Updating Velopack CLI from $installedVpkVersion to $velopackCliVersion..."
  Invoke-Native -FilePath dotnet -ArgumentList @("tool", "update", "-g", "vpk", "--version", $velopackCliVersion)
}

$vpkExe = Join-Path (Join-Path $env:USERPROFILE ".dotnet") "tools\vpk.exe"
if (-not (Test-Path -LiteralPath $vpkExe)) {
  throw "vpk CLI was not found after install. Ensure the .NET global tools directory is available."
}

# Fetch the previous release's full nupkg into $velopackOut first (build-velopack-previous-release.ps1),
# so `vpk pack` (below) finds it there on its own and emits a delta nupkg alongside the full one at no
# extra flag — --delta defaults to BestSpeed.
if ($PreviousReleaseRepoUrl) {
  Start-BuildPhase "Fetch previous release for delta"
  Get-WeirPreviousReleaseFullNupkg -RepoUrl $PreviousReleaseRepoUrl -Version $PreviousReleaseVersion -OutputDir $velopackOut -VpkExePath $vpkExe
}

Start-BuildPhase "vpk pack"
Write-Host "Running vpk pack..."
Invoke-Native -FilePath $vpkExe -ArgumentList @(
  "pack",
  "--packId", "Weir",
  "--packVersion", $buildVersion,
  "--packDir", $packDir,
  "--mainExe", "Weir.exe",
  "--outputDir", $velopackOut,
  "--icon", (Join-Path $PSScriptRoot "assets\\weir-tray-icon.ico")
)

Write-Host ""
Write-Host "Velopack packaging output:"
Get-ChildItem -Path $velopackOut | Select-Object Name, Length
$deltaPackages = @(Get-ChildItem -Path $velopackOut -Filter "*-delta.nupkg")
if ($deltaPackages.Count -gt 0) {
  Write-Host "Delta package(s) built against the previous release: $($deltaPackages.Name -join ', ')"
} elseif ($PreviousReleaseRepoUrl) {
  Write-Host "No delta package was built (no previous full release was found to diff against); the full package is the fallback."
}

# The previous release's full nupkg, fetched above only so vpk could build the delta against it, is
# still sitting in $velopackOut and listed in its feed files alongside $buildVersion's own packages.
# Remove it before anything here gets uploaded (#804): a client on the previous version already has
# that release's own full package; it only needs this release's delta.
# scripts/check-release-assets-single-version.mjs re-checks this in release.yml, right before upload.
if ($PreviousReleaseRepoUrl) {
  Start-BuildPhase "Prune stale release feed entries"
  Invoke-Native -FilePath node -ArgumentList @(
    (Join-Path $repoRoot "scripts\\prune-release-feed.mjs"),
    "--output-dir", $velopackOut,
    "--version", $buildVersion
  )
}

Write-BuildPhaseSummary
