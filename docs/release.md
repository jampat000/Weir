# Weir releases

Each tagged release produces three deliverables:

1. a GitHub Release for the tagged source snapshot
2. a Windows desktop package (Velopack installer + delta update files)
3. a Docker image published to GitHub Container Registry

The Windows artifact is a desktop app with a .NET tray host and Velopack for delta updates. It is not a Windows service.

Weir is released under AGPL-3.0-or-later. Release artifacts are built from the tagged source tree and remain subject to that license.

## Version scheme

Weir uses plain SemVer, the same as Deluno: `X.Y.Z`, with `-rc.N` (or another pre-release name such as
`-beta.N`) on the end until the version is final. A fix raises the last number (`1.0.1`), a feature the
middle one (`1.1.0`), a breaking change the first (`2.0.0`). `1.0.0-rc.1`, `1.0.0-rc.2` and
`1.0.0` come in that order, and `1.0.0-rc.10` is newer than `1.0.0-rc.9`. Build metadata
(`1.0.0+abc`) is not accepted in a tag.

## Contract

There is no version-bump PR (#804): no file in the tree carries the release version. Every build
that ships (the Windows package, the Docker image) stamps its own version on the command line, taken
from the tag itself — `WeirVersion` in `apps/server/Directory.Build.props` is a fixed placeholder that
never changes. Cutting a release is:

1. Pick a commit on `main` whose `CI / ci-passed` run has already passed — normally just the current
   `main` HEAD, once its own push run is green.
2. Create user-facing release notes for the target tag and merge them to `main` as a normal PR:

   - Create `docs/release-notes/vX.Y.Z.md` (or `vX.Y.Z-rc.N.md` for a release candidate) using `docs/release-notes/TEMPLATE.md`.
   - Keep wording operator-friendly and focused on what changed for users.

   For a stable release, change only the notes file and `CHANGELOG.md`: the docs, README and
   `compose.yaml` show pinned versions as `X.Y.Z` and point at `latest`, so they never need editing.
   Until 1.0.0, no image carries `latest` for a new release candidate, so the Docker examples name the
   candidate itself: in the same PR, replace the previous candidate's version (for example
   `1.0.0-rc.1`) with the new one in `compose.yaml`, `docker/.env.example`, `README.md`,
   `docker/README.md`, `docs/docker.md`, `docs/install-docker.md` and `docs-site/docs`
   (`git grep -l <previous version>` lists them). At 1.0.0 they go back to `latest`. Touching `compose.yaml`,
   `Dockerfile`, `docker/**` or `packaging/**` makes CI run both package smokes, on the PR and again on
   `main`, and the release is not tagged until that run on `main` is green (it does not wait for one).

   This PR touches nothing under `apps/`, `packaging/` or `Dockerfile`, so `CI / ci-passed` on it and
   on its merge to `main` both finish in well under a minute (path-aware CI skips everything but the
   repository checks).
3. For a stable release, run the golden path on that exact commit and record that it passed
   ([Golden path before tagging](#golden-path-before-tagging)). The release refuses to publish without it. A release
   candidate skips this step.
4. Create an annotated tag on that merge commit:

   ```bash
   git fetch origin
   git checkout main
   git pull origin main
   git tag -a vX.Y.Z -m "Weir vX.Y.Z"
   git push origin vX.Y.Z
   ```

5. Pushing `v*` triggers `.github/workflows/release.yml`. Its `ci-passed` job confirms `CI` already
   passed on that exact commit (`scripts/verify-ci-for-release.mjs`) instead of re-running it, its
   `golden-path` job confirms the golden path passed on it too (`scripts/verify-golden-path-for-release.mjs`), and
   `windows-smoke` validates the tag itself is a well-formed `X.Y.Z` or `X.Y.Z-rc.N` version
   (`scripts/check-release-version.mjs`) before stamping it onto the server, the tray, the Windows
   package and the Docker image.
6. The release workflow requires `docs/release-notes/<tag>.md` for the tag and publishes that file as the GitHub Release body.

### Golden path before tagging

Green tests have shipped bugs that only showed when the product was used (a pause that did not pause, seeding
originals deleted, a file cleaned twice). So before a tag is made, the exact commit is installed on a clean machine
and used the way a person uses it, and the release will not publish unless that run is on record. Decided by the owner,
7 Oct 2026 (#903; Deluno's half is Deluno#1158, and the mechanism is the same in both).

This holds for stable releases. A release candidate (`v1.0.0-rc.4`) is tagged as soon as CI passes on its commit:
Weir is independent of Deluno, so a Weir fix ships at once and the real-data test keeps running on it. The rig's live test is its proof, and the golden path runs on the next stable release.

**1. Get the exact commit's build.** CI builds the Windows package for any commit without a tag and keeps it for
7 days as the workflow artifact `weir-windows-<short sha>` (the first 7 characters of the commit), holding
`Weir-win-Setup.exe` and `Weir-win-Portable.zip`.

- A push to `main` builds it when the change touched code, packaging or a workflow. A commit that changed only
  documents or release notes (which is what a release commit normally is) builds nothing, and neither does a pull
  request run. To build any commit or branch, run CI by hand, which skips nothing. For the golden path, give it
  the release the commit is meant to become:

  ```bash
  gh workflow run ci.yml --repo jampat000/Weir --ref main -f version=1.0.0-rc.4
  ```

  `--ref` takes a branch or a tag; for a commit that no branch points at, push a branch at it first.
- Find the run, check it is for the commit you mean, and download it:

  ```bash
  gh run list --repo jampat000/Weir --workflow ci.yml --commit <full sha> --json databaseId,event,conclusion
  gh run view <run id> --repo jampat000/Weir --json headSha
  gh run download <run id> --repo jampat000/Weir -n weir-windows-<short sha> -D weir-build
  ```

- Given a version, the build reports that version with the commit as build metadata, such as
  `1.0.0-rc.4+abc1234`: in `Weir.exe --version`, in System › About, and in the capabilities answer Deluno shows.
  The golden-path record names it. Update checks ignore the `+abc1234` part, so the build counts as the release
  itself. Without a version, or on a push, it reports `0.0.1-dev`, the placeholder in
  `apps/server/Directory.Build.props`. Either way the artifact's name and its run's `headSha` tie it to the
  commit, and it is never published.

**2. Run it on the clean VM.** The Deluno session drives the run on a Hyper-V Windows VM that is reverted to its
saved clean checkpoint before every run, with Deluno's installer and this build's `Weir-win-Setup.exe` both built from
the commit being tagged. The Weir session checks Weir's side through Weir's UI and read-only, against one shared
checklist. The rig stays the long-running real-data box and is not the golden path.

**3. The checklist.** [golden-path.md](golden-path.md) is the one checklist for both products, in run order,
the same word for word in Deluno. Each line is checked by clicking or watching, and the evidence (what was seen,
screenshots, log lines) goes in the filled checklist that the pass record points at.

**4. Record the result.** Whoever drove the run sets a commit status named `golden-path` on the full SHA, with a
description and a link to the evidence comment:

```bash
gh api repos/jampat000/Weir/statuses/<full sha> \
  -f state=success -f context=golden-path \
  -f description="Golden path passed on the clean VM, 8 of 8 checks" \
  -f target_url="https://github.com/jampat000/Weir/issues/<n>#issuecomment-<id>"
```

A run that fails is recorded the same way with `-f state=failure`. The newest status for the context is the one that
counts, so a later failure withdraws an earlier success. A status belongs to one commit: a fix made after a failed run
is a new commit, and so is a commit that changes only the release notes, and each needs its own run on record.

**5. The release checks it.** The `golden-path` job in `release.yml` (`scripts/verify-golden-path-for-release.mjs`)
passes only when the tagged commit's newest `golden-path` status is `success`. It does not wait. Without one the
release stops there, before anything is published, with: "This commit has no passing golden-path run. Run the golden
path on this exact build (see docs/release.md), then re-run the release." Record the result, then re-run the
release's failed jobs.

### Cutting a release candidate

A release candidate is cut exactly like a release, with a pre-release tag:

```bash
git tag -a v1.0.0-rc.1 -m "Weir v1.0.0-rc.1"
git push origin v1.0.0-rc.1
```

The release workflow handles it end to end:

- the GitHub Release is published as a **pre-release** (`prerelease: true`), so it never shows as "Latest" on
  the Releases page; a plain `vX.Y.Z` tag publishes a normal release
- the Windows package, the server, the tray and the Docker image all carry `1.0.0-rc.1`
  (`WeirVersion.Resolve` keeps the pre-release part; .NET's numeric assembly version is `1.0.0.0`, and the
  full text is the informational version)
- the Docker image is tagged `1.0.0-rc.1` and nothing else: a release candidate moves no moving tag, so `latest`
  and `major.minor` (`1.0`) first appear with the stable `1.0.0`, and a later release candidate never
  replaces the image people run by default. Pull a release candidate by its version tag
- the Windows delta is built against the newest published release that is older than this one
  (`scripts/find-previous-release.mjs`), release candidates included, so `rc.2` is a delta from `rc.1` and
  `1.0.0` from the last `rc`. With nothing older published, only the full package is produced

After the release candidates comes `1.0.0`, then `1.0.1` for fixes, `1.1.0` for features and `2.0.0` for
breaking changes.

### Which version an install is offered

Weir's own update check reads the GitHub release list (not `/releases/latest`, which never returns a
pre-release) and offers the newest release by SemVer precedence, skipping drafts. An install running a
pre-release is offered pre-releases and stable releases; an install running a stable version is offered stable
releases only. The tray's Velopack update source follows the same rule. Docker installs are never updated in
place: System › About names the newest tag and the pull command.

Local Docker is not required for this release path. Docker build, publish,
manifest verification, and container smoke testing all run on GitHub-hosted
Actions runners.

## What the release workflow does

The `Release` workflow:

- **`ci-passed`**: confirms that `.github/workflows/ci.yml` passed on the exact tagged commit
  (`scripts/verify-ci-for-release.mjs`), instead of running those tests a second time. It accepts
  only a `push` run on `main` or a manual run, judged by its latest attempt, in which the server
  build and tests (Linux and Windows), the web checks and every required contract area actually
  ran and passed; a job the path filter skipped does not count. It does not wait: a tag made
  while CI is still running fails at once, and the fix is to wait for CI and re-run the release.
  If no run proves the commit (the tag is on a commit that never ran CI, CI failed or was
  cancelled, or a push to `main` skipped the tests because nothing they cover changed), it fails
  with the fix: run the full CI on the tag, wait for it, then re-run the release's failed jobs:

  ```bash
  gh workflow run ci.yml --ref vX.Y.Z
  ```

- **`golden-path`**: for a stable tag, confirms the golden path passed on the exact tagged commit (a release
  candidate passes it at once): the newest commit status
  with the context `golden-path` is `success` (`scripts/verify-golden-path-for-release.mjs`). It reads
  statuses only and does not wait. See [Golden path before tagging](#golden-path-before-tagging).

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
- **`publish-windows`**, once `ci-passed`, `golden-path`, `validate` and `windows-smoke` have passed:
  - when the repository variable `ATTEST_PROVENANCE` is `true`, records a signed provenance attestation for the
    release files (the Windows files and `weir-web-dist.zip`)
  - creates the GitHub Release from `docs/release-notes/<tag>.md`, with those files attached
- **`publish-docker`**, once `ci-passed`, `golden-path`, `validate`, `docker-candidate` and `docker-arm64` have passed:
  - builds and pushes Docker tags for linux/amd64 and linux/arm64:
    - `ghcr.io/<owner>/<repo>:X.Y.Z` (the git tag is `vX.Y.Z`; the image tag drops the `v`, and a pre-release keeps its suffix: `1.0.0-rc.1`)
    - the image carries its provenance and an SBOM from the build
  - when `ATTEST_PROVENANCE` is `true`, records a signed provenance attestation for the pushed image's digest
    (the multi-architecture manifest list, kept in ghcr beside the image)
  - verifies the published Docker manifest resolves
  - runs the published Docker image and waits for `/health`
  - only then moves the moving tags `ghcr.io/<owner>/<repo>:X.Y` and `:latest` to the version's image, and
    only for a stable release: a release candidate moves neither

Everything before the two publish jobs runs at the same time. The publish jobs do not wait for each other: a slow
or failed Docker build never holds back the GitHub Release that installed Weirs update from, and either job can be
re-run alone (`gh run rerun <run id> --job <job id>`). Only `publish-windows` has `contents: write`, and only
`publish-docker` holds registry credentials and `packages: write`. The registry login and Docker push occur only
after the CI proof, the release checks, the unpushed candidate's complete live audit and the arm64 build have passed,
and the GitHub Release is created only after the CI proof, the release checks and the Windows package have passed
(`scripts/check-release-workflow-gates.mjs` enforces this).
The published image is rebuilt from the candidate jobs' cached layers. A failed screen, API check, browser console error, page
error, failed request, bad response, changed pass-through output, or incomplete
source cleanup therefore stops the release before the versioned image or a
moving tag is published. The Windows package smoke runs the same real pass-through
lifecycle against the packaged executable and bundled FFmpeg.

`VITE_SUPPORT_URL` is a Vite build-time variable. Official releases should set the GitHub Actions repository variable `VITE_SUPPORT_URL` to `https://github.com/sponsors/jampat000` so the production frontend and packaged Windows installer include the Support section of **System › About**. If that variable is missing, release builds still succeed, and production hides the Support section.

## Registry authentication

The release workflow publishes GHCR images, in `publish-docker`, with the repository `GITHUB_TOKEN` and
`packages: write` permission. No personal access token is required for normal releases, and no
workflow reads a `GHCR_TOKEN` secret.

## Timeouts, permissions and concurrency

- **Every job has a `timeout-minutes`**, about two to three times its slowest recent successful run
  with a floor of 10, so a hung job fails in minutes instead of holding a runner for the six-hour
  default. The release's `ci-passed` and `golden-path` jobs have 10: they only ask GitHub about the tagged commit and do not
  wait for anything. Raise a limit in the same change that makes a job genuinely slower.
- **Every workflow starts at `permissions: contents: read`.** A job adds only what it needs: `changes`
  adds `pull-requests: read`; the release's `ci-passed` adds `actions: read` and its `golden-path` adds `statuses: read`; CodeQL's `analyze` adds `security-events: write`; `publish-windows` alone adds `contents: write`,
  `publish-docker` alone adds `packages: write`, and both add `id-token: write` and `attestations: write`.
- **A release is never cancelled once started.** `release.yml` serialises runs per tag with
  `cancel-in-progress: false`: a second run for the same tag waits for the first, so a publish job cannot be
  stopped between pushing the image and moving the moving tags, or part-way through creating the GitHub Release. Pull request runs of `ci.yml`,
  CodeQL and the docs build do cancel when superseded; a push to `main` never does.
- **Dependabot is security-only.** `.github/dependabot.yml` sets `open-pull-requests-limit: 0` for every
  ecosystem, so there are no routine version pull requests; Dependabot security updates still open them.

## Release artifacts

| Deliverable | Meaning |
|-------------|---------|
| `Tag + source tree` | Canonical source snapshot for the release. |
| `weir-web-dist.zip` | Static production build of `apps/web/dist`. The Weir server is still required. |
| `Weir-win-Setup.exe` | Windows desktop installer (Velopack) with .NET tray host, bundled .NET server (`server\WeirServer.exe`), bundled web UI, bundled FFmpeg, and delta update support. |
| `ghcr.io/<owner>/<repo>:X.Y.Z` | Versioned all-in-one container image (linux/amd64 and linux/arm64), with a signed provenance attestation when the repository variable `ATTEST_PROVENANCE` is `true` (`gh attestation verify oci://ghcr.io/<owner>/<repo>:X.Y.Z --repo <owner>/<repo>`). |
| `ghcr.io/<owner>/<repo>:X.Y` and `:latest` | Moving tags for the newest stable container image. A release candidate publishes only its version tag. |

## Windows package

The Velopack-based Windows package is the supported Windows release artifact. Release builds produce a setup exe, full nupkg, and delta nupkg under `dist/windows/releases/`.

The delta nupkg is built by `packaging/windows/build-velopack.ps1 -PreviousReleaseRepoUrl <repo>
-PreviousReleaseVersion <X.Y.Z or X.Y.Z-rc.N>`, which downloads that GitHub Release's full nupkg (by its
tag, with `gh release download`) into the output directory before `vpk pack` runs; `vpk pack` then finds
it there on its own and emits a delta package alongside the full one. `release.yml` picks the version
itself (`scripts/find-previous-release.mjs`): the newest published release that is older than the one
being released by SemVer precedence, release candidates included. A release that is not older (a newer
version that is somehow still published, or the same tag re-run) is never the base. The downloaded file
is kept in `packaging/windows/vendor/previous-release`, keyed on that version. If there is no older
release to diff against (the very first release, or a gap in the chain), only the full package is
produced — every install can always fall back to it. Local and PR builds omit `-PreviousReleaseRepoUrl` and never fetch anything or
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

Docker images, pre-releases included, are published from the same tag workflow.

If Docker Desktop is broken or not installed locally, do not block the release
on this workstation. Run the remote validation workflow instead:

```powershell
.\scripts\verify-docker-remote.ps1
```

That command triggers the `CI` workflow for the current ref and watches it.
The Docker image build and Docker smoke test run on GitHub infrastructure.

Pull and run:

```bash
docker pull ghcr.io/jampat000/weir:1.0.0-rc.1
docker run --rm \
  -p 9347:9347 \
  -v weir-data:/data/weir \
  ghcr.io/jampat000/weir:1.0.0-rc.1
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
