# Weir releases

Each tagged release produces three deliverables:

1. a GitHub Release for the tagged source snapshot
2. a Windows desktop package (Velopack installer + delta update files)
3. a Docker image published to GitHub Container Registry

The Windows artifact is a desktop app with a .NET tray host and Velopack for delta updates. It is not a Windows service.

Weir is released under AGPL-3.0-or-later. Release artifacts are built from the tagged source tree and remain subject to that license.

## Contract

There is no version-bump PR (#804): no file in the tree carries the release version. Every build
that ships (the Windows package, the Docker image) stamps its own version on the command line, taken
from the tag itself — `WeirVersion` in `apps/server/Directory.Build.props` is a fixed placeholder that
never changes. Cutting a release is:

1. Pick a commit on `main` whose `CI / ci-passed` run has already passed — normally just the current
   `main` HEAD, once its own push run is green.
2. Create user-facing release notes for the target tag and merge them to `main` as a normal PR:

   - Create `docs/release-notes/vX.Y.Z.md` using `docs/release-notes/TEMPLATE.md`.
   - Keep wording operator-friendly and focused on what changed for users.

   Change only the notes file and `CHANGELOG.md`. The docs, README and `compose.yaml` show pinned
   versions as `X.Y.Z` on purpose, so they never need editing for a release. Touching `compose.yaml`,
   `Dockerfile`, `docker/**` or `packaging/**` makes CI run both package smokes, on the PR and again on
   `main`, which the release then waits for.

   This PR touches nothing under `apps/`, `packaging/` or `Dockerfile`, so `CI / ci-passed` on it and
   on its merge to `main` both finish in well under a minute (path-aware CI skips everything but the
   repository checks).
3. Create an annotated tag on that merge commit:

   ```bash
   git fetch origin
   git checkout main
   git pull origin main
   git tag -a vX.Y.Z -m "Weir vX.Y.Z"
   git push origin vX.Y.Z
   ```

4. Pushing `v*` triggers `.github/workflows/release.yml`. Its `ci-passed` job confirms `CI` already
   passed on that exact commit (`scripts/verify-ci-for-release.mjs`) instead of re-running it, and
   `windows-smoke` validates the tag itself is a well-formed `X.Y.Z` version
   (`scripts/check-release-version.mjs`) before stamping it onto the server, the tray, the Windows
   package and the Docker image.
5. The release workflow requires `docs/release-notes/vX.Y.Z.md` for the tag and publishes that file as the GitHub Release body.

Local Docker is not required for this release path. Docker build, publish,
manifest verification, and container smoke testing all run on GitHub-hosted
Actions runners.

## What the release workflow does

The `Release` workflow:

- **`ci-passed`**: confirms that `.github/workflows/ci.yml` passed on the exact tagged commit
  (`scripts/verify-ci-for-release.mjs`), instead of running those tests a second time. It accepts
  only a `push` run on `main` or a manual run, judged by its latest attempt, in which the server
  build and tests (Linux and Windows), the web checks and every required contract area actually
  ran and passed; a job the path filter skipped does not count. If that run is still going, it
  waits for it (up to 45 minutes). If no run proves the commit (the tag is on a commit that never
  ran CI, CI failed or was cancelled, or a push to `main` skipped the tests because nothing they
  cover changed), it fails with the fix: run the full CI on the tag, then re-run the release's
  failed jobs:

  ```bash
  gh workflow run ci.yml --ref vX.Y.Z
  ```

- **`validate`**: what CI cannot have checked. The release notes file exists, the release gate
  ordering holds, a NuGet vulnerability scan as of today, the E2E smoke, and the production web
  build published as `weir-web-dist.zip`
- builds the Velopack Windows package on `windows-latest`
- publishes `weir-web-dist.zip`
- builds a local, unpushed Docker release candidate
- runs the complete packaged browser/API audit against that candidate, including
  a mounted disposable Processing file that must pass through byte-identically into
  the processed tree before its watched source is removed, and uploads screenshots
  plus JSON evidence
- builds and pushes Docker tags for linux/amd64 and linux/arm64:
  - `ghcr.io/<owner>/<repo>:X.Y.Z` (the git tag is `vX.Y.Z`; the image tag drops the `v`)
  - `ghcr.io/<owner>/<repo>:latest`
- verifies the published Docker manifest resolves
- runs the published Docker image and waits for `/health`
- creates the GitHub Release

All of those run at the same time. Only `publish` holds registry credentials and write
permissions, and it needs every other job, so the registry login and Docker push occur only
after the CI proof, the release checks, the Windows package and the unpushed candidate's
complete live audit have all passed (`scripts/check-release-workflow-gates.mjs` enforces this).
The published image is rebuilt from the candidate jobs' cached layers. A failed screen, API check, browser console error, page
error, failed request, bad response, changed pass-through output, or incomplete
source cleanup therefore stops the release before either the versioned image or
`latest` is published. The Windows package smoke runs the same real pass-through
lifecycle against the packaged executable and bundled FFmpeg.

`VITE_SUPPORT_URL` is a Vite build-time variable. Official releases should set the GitHub Actions repository variable `VITE_SUPPORT_URL` to `https://github.com/sponsors/jampat000` so the production frontend and packaged Windows installer include the Support section of **System › About**. If that variable is missing, release builds still succeed, and production hides the Support section.

## Registry authentication

The release workflow publishes GHCR images with the repository `GITHUB_TOKEN` and
`packages: write` permission. No personal access token is required for normal releases.

## Release artifacts

| Deliverable | Meaning |
|-------------|---------|
| `Tag + source tree` | Canonical source snapshot for the release. |
| `weir-web-dist.zip` | Static production build of `apps/web/dist`. The Weir server is still required. |
| `Weir-win-Setup.exe` | Windows desktop installer (Velopack) with .NET tray host, bundled .NET server (`server\WeirServer.exe`), bundled web UI, bundled FFmpeg, and delta update support. |
| `ghcr.io/<owner>/<repo>:X.Y.Z` | Versioned all-in-one container image (linux/amd64 and linux/arm64). |
| `ghcr.io/<owner>/<repo>:latest` | Latest stable container image. |

## Windows package

The Velopack-based Windows package is the supported Windows release artifact. Release builds produce a setup exe, full nupkg, and delta nupkg under `dist/windows/releases/`.

The delta nupkg is built by `packaging/windows/build-velopack.ps1 -PreviousReleaseRepoUrl <repo>
-PreviousReleaseVersion <X.Y.Z>`, which fetches the previous GitHub Release's full nupkg into the
output directory before `vpk pack` runs; `vpk pack` then finds it there on its own and emits a delta
package alongside the full one. `release.yml` resolves `-PreviousReleaseVersion` itself (the latest
published release, via the GitHub API) and caches that one file across runs, keyed strictly on that
version, so a release only ever downloads it once. If there is no previous release to diff against (a
gap in the chain, or the very first release), only the full package is produced — every install can
always fall back to it. Local and PR builds omit `-PreviousReleaseRepoUrl` and never fetch anything or
produce a delta, so they stay offline and fast.

`vpk pack` also carries that downloaded previous-version nupkg forward into its own feed files
(`releases.win.json`, the legacy `RELEASES`), since it has no reason to know it should not. Right
after packing, `build-velopack.ps1` removes that package and rewrites both feed files to list only the
version being released (`scripts/prune-release-feed.mjs`); `release.yml` re-checks the result before
upload (`scripts/check-release-assets-single-version.mjs`) and fails the release if anything for another
version is still there. A client already on the previous version has that version's own full package
cached locally from when it installed or last updated, and needs only this release's delta; an older
client chains deltas across the feed entries of the releases in between instead.

If you build the Windows package locally and want the installer to include the Support section of **System › About**, set `VITE_SUPPORT_URL` before running `packaging/windows/build-velopack.ps1`:

```powershell
$env:VITE_SUPPORT_URL = "https://github.com/sponsors/jampat000"
powershell -ExecutionPolicy Bypass -File packaging/windows/build-velopack.ps1
```

After installing:

1. Launch `Weir` from the Start Menu or desktop shortcut.
2. Weir starts in the user session, not as a Windows service.
3. The .NET tray app (`Weir.exe`) launches the Weir server (`server\WeirServer.exe`) as a child process, watches it, and restarts it if it stops.
   Every deliberate stop (the LAN access toggle, Change port, Restart to update, Quit, and `--allow-lan` on a running Weir) asks the server to stop by setting its named event `Local\Weir-Stop-<server pid>` and waits up to 10 s for it to exit, so hosted services, running jobs and the database close in order. Only a server that does not exit in time, or cannot be asked, is killed. `tray-host.log` says which happened: `stopped cleanly in 0.3 s` or `did not stop in 10.0 s; killing it`. The event is per user and per Windows session; the server has no HTTP route that stops it.
4. The tray icon opens the local app in the browser and exposes `Open Weir`, `Open Data Folder`, `Change port`, `Check for updates`, and `Quit`.
5. Application binaries install under `%LocalAppData%\Weir` (per-user, no admin required).
6. The local runtime root is created under `C:\ProgramData\Weir`.

Updates are handled by the .NET tray app via Velopack. Delta updates keep downloads small and rollback is automatic on failure. No separate updater service is needed.

Downloading an update never installs it, and the tray never leaves Velopack's installer waiting while Weir keeps running: that installer stops Weir and its server after about 60 s (#857). A downloaded update installs in exactly these cases, and each one stops the server cleanly first (the stop above, `stopped cleanly` in `tray-host.log`), so the installer only ever finds a tray that is about to end:

- **Quit** from the tray icon stops the server, then installs the update and does not start Weir again.
- **Restart to update** (the tray menu item, the balloon, or **Restart to apply** on System › About) stops the server, installs the update and starts Weir again without opening the browser.
- **Weir has been idle for 5 minutes**, in **Auto** mode only (#875). It is the same restart as **Restart to update**, started by the tray when nobody is there to press it, so a Weir left running for weeks still updates. See [Installing when idle](#installing-when-idle).
- **The next start.** When Windows ends the session or the tray is killed, so Quit never runs, the downloaded package stays on disk. The next tray start installs it before the port is chosen and before the server starts, silently, then Weir starts again on the new version. Each update is tried once this way (`update-start-attempt` in the data folder holds the version), so an install that fails cannot loop, and a person who chose **Notify only** never gets one. The log line `Update vX was left waiting to install. Installing it before the server starts.` marks it. This is the only install at start-up: the tray turns off Velopack's own start-up auto-apply (`SetAutoApplyOnStartup(false)`), which would otherwise install the package first and skip all of those checks (#865).

**Download only** downloads in the background and shows a notice you can click to restart and install. It installs when you do that, on **Restart to apply**, or on the next quit or start, and never by itself while Weir runs. **Notify only** downloads nothing. A `--silent` start shows no notice and installs the same ways.

### Installing when idle

Once an update is downloaded in **Auto** mode the tray waits for Weir to be idle: no file pass running, none being handed back, and none queued that could start, for 5 minutes in a row. Work starting during the wait starts the 5 minutes again. Queued work that cannot start, because its workflow is switched off, outside its schedule window or paused, does not count as busy; queued work whose schedule window is open does, and so does a pass that is running when its window closes. Scans and clean-up sweeps are not file work. Then the tray restarts Weir exactly as **Restart to update** does: a clean stop of the server, a silent install, and a start with `--no-browser` on the saved port.

`tray-host.log` follows it:

- `Update vX downloaded; installing once Weir has been idle for 5 minutes.`
- `Weir is idle. ...`, `Weir has work to do. ...` or `Weir has not said whether it is idle. ...`, each time the answer changes.
- `Weir has been idle for 5 minutes; installing update vX now.`
- `Update vX is waiting, but the update mode is now DownloadOnly, so Weir does not install it by itself.` The choice is read again just before installing, so switching away from **Auto** while an update waits stops the install.

How the tray learns Weir is idle: while `update-state.json` says an update is downloaded, the server rewrites `work-state.json` in the data folder every 15 seconds with `busy` and the time it looked (`checkedAt`). Nothing is written when no update is waiting, so a Docker install never writes it. The tray installs only on a fresh answer of idle; a missing, unreadable or over-a-minute-old file (the server stopped, restarting or stuck) counts as not idle and restarts the 5 minutes. It is a file and not an HTTP route on purpose: the tray has no signed-in session, an unauthenticated route would be reachable from other devices whenever LAN access is on, and the data folder is readable only by the account running Weir. What counts as file work is decided by the same rules the worker slots use to lease a job (`ProcessingJobStore.HasFileWorkAsync`), so "could start" and "would start" cannot differ.

If an operator runs a manually staged copy without Velopack install metadata, the
tray keeps `Check for updates` visible and sends it to the browser-based release check
on **System › About**. It must not silently remove the update action.

This design is intentional. Running in the user session avoids common NAS or external-drive access issues that affect Windows services, while keeping writable configuration, logs, backups, and the SQLite database out of the application install directory.

### Installing Weir from another program

`Weir-win-Setup.exe` run plain assumes a person is at the interactive desktop, both for its own
install UI and for the app launch it does afterward. A program driving Weir unattended (another
installer, a provisioning script) must pass `--silent` instead, or Setup can hang indefinitely with
no window and no exit code to check (#779):

```
Weir-win-Setup.exe --silent
```

Exits 0 on success, non-zero on failure, within seconds — `--silent` also skips Velopack's
post-install app launch, so nothing here waits on Weir itself. Start Weir explicitly afterward, and
watch for it becoming ready rather than for the process to exit — it is a foreground app that keeps
running once started, exactly like a person's own copy:

```
"%LocalAppData%\Weir\current\Weir.exe" --port 9400 --silent
```

`--silent` on `Weir.exe` guarantees no UI at all — no port dialog, no error message box, no browser
tab — regardless of whether the session looks interactive. Poll `GET http://127.0.0.1:9400/ready`
until it answers `{"ready": true}` (a healthy start typically takes a few seconds; 60 seconds is a
generous timeout). An early exit means the start failed; its exit code is non-zero and
`tray-host.log` under the runtime home (`C:\ProgramData\Weir` by default) says why.

**Don't wait on the process tree.** Weir keeps running after Setup exits — that is correct, not a
hang — so a caller must wait for Setup's own exit (or, for the second command above, for `/ready` or
`/health` to answer), never for "the output stream closed" or "every process this started has
exited" as its signal. If a caller redirects a launched process's stdout/stderr through pipes (the
ordinary way to capture output — `Process.StandardOutput`/`StandardError` in .NET,
`subprocess.communicate()` in Python, and similar in most languages), Windows only signals
end-of-file on those pipes once every process holding a duplicate of the write end has closed it,
including whatever that process went on to start. Weir.exe closes any stdio handles it inherited
before it does anything else, specifically so it is never the process still holding a caller's pipe
open (`apps/tray/Weir.Tray/InheritedStdioHandles.cs`); `scripts/smoke-windows-package.ps1` proves
this against the real `Weir-win-Setup.exe` and the real installed `Weir.exe`, both piped the way a
capturing caller would.

A silent install never shows the one-time Windows admin (UAC) prompt Weir otherwise asks to create its firewall
rule for LAN access, and without `--allow-lan` Weir is local-only: the server listens on `127.0.0.1` and `[::1]`
and nothing else, so no other device on the network can connect. A program that needs LAN access runs the
installed `Weir.exe --allow-lan`: it always turns LAN access on (a Weir that is already running restarts within a
few seconds to listen for the network), with no prompt of any kind. From a process that is already elevated it
also creates the firewall rule; it never tries to elevate itself. An unelevated caller still gets LAN access
turned on and a zero exit code, plus a log line saying the rule was not created, so Windows Firewall decides
whether other devices get through. Full detail:
[Windows Installer → Firewall and LAN access](https://github.com/jampat000/Weir/blob/main/docs-site/docs/deployment/windows.md#firewall-and-lan-access).

Full detail, including `WEIR_PORT` as an alternative to `--port`: [Windows Installer → Installing
Weir from another program](https://github.com/jampat000/Weir/blob/main/docs-site/docs/deployment/windows.md).

## Docker

Stable Docker releases are published from the same tag workflow.

If Docker Desktop is broken or not installed locally, do not block the release
on this workstation. Run the remote validation workflow instead:

```powershell
.\scripts\verify-docker-remote.ps1
```

That command triggers the `CI` workflow for the current ref and watches it.
The Docker image build and Docker smoke test run on GitHub infrastructure.

Pull and run:

```bash
docker pull ghcr.io/jampat000/weir:latest
docker run --rm \
  -p 9347:9347 \
  -v weir-data:/data/weir \
  ghcr.io/jampat000/weir:latest
```

Or use the root `compose.yaml`:

```bash
docker compose pull
docker compose up -d
```

No env file is required for the default all-in-one container path. Create `.env.weir`
only if you want to override defaults such as the image tag or runtime home.

## Not shipped

- Windows service mode
- Windows installer code signing
- NuGet publishing
- npm publishing
- automatic version bumps or release bots

## Related files

- `.github/workflows/ci.yml`
- `.github/workflows/release.yml`
- `docs/release-governance.md`
- `docs/smoke-checklists.md`
- `docker/README.md`
- `docs/local-development.md`
