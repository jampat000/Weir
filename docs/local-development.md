# Weir — local development (server + web)

This **Weir** repository contains **`apps/server`** (the C# / .NET 10 server: HTTP API, **SQLite**, cookie sessions, the job queue and Processing), **`apps/web`** (React/Vite) and **`apps/tray`** (the Windows tray app). Media manager connections (Sonarr, Radarr, Deluno, or anything posting Weir's own payload) live under **Setup › Connections › Media managers**; inbound events all arrive at `POST /api/v1/intake/webhook/{source}`. See [ADR-0013](adr/ADR-0013-media-managers-are-kinds-not-products.md).

**Local web/API ports** are versioned in **`scripts/dev-ports.json`**; the policy is summarized in **[`docs/ports.md`](ports.md)**.

## Prerequisites

- **.NET 10 SDK** (pinned in **`apps/server/global.json`**)
- **Node.js** (pinned in **`.node-version`**; npm on `PATH`)
- **Python 3.13+** only for the contract suite and E2E test runners (they judge the server from outside; nothing in Weir runs on Python)

## One-time setup (after cloning)

Activate the pre-push hook so the contract suite lint, prettier, the dead-code guard and the OpenAPI types drift check run locally before every push:

```powershell
git config core.hooksPath .githooks
```

The hook delegates to `scripts/pre-push.mjs` (a plain Node script; run it by hand with `node scripts/pre-push.mjs`). It skips checks gracefully if `ruff` or `apps/web/node_modules` are not yet installed — set those up first for full coverage.

The server persists state in **file-backed SQLite** under **`WEIR_HOME`** and creates or migrates its database itself when it starts; there is no separate migration command.
Docker Desktop is not required for normal local development or for shipping a release.

## `.env` (local)

Copy **`.env.example`** → **`.env`** at the repository root.

Required for auth and `/api/v1`:

- **`WEIR_SESSION_SECRET`** — long random value.

Optional path overrides (defaults are under the OS-specific **`WEIR_HOME`**):

- **`WEIR_HOME`**, **`WEIR_DB_PATH`**, **`WEIR_BACKUP_DIR`**, **`WEIR_LOG_DIR`**, **`WEIR_TEMP_DIR`**

The server itself reads **`WEIR_*`** from its environment only. The dev launchers (**`scripts/dev-backend.ps1`**, **`scripts/dev.ps1`**, **`npm run dev`**) load **`.env`** into that environment first; shell variables still override.

**`WEIR_ENV`** defaults to **`production`** when unset, so a source or systemd install gets ASP.NET's production error handling (no developer exception page) out of the box. The dev launchers above set **`WEIR_ENV=development`** explicitly; do the same if you run the server manually and want the developer exception page.

## Server (`apps/server`)

Build and test from the repo root ([ADR-0017](adr/ADR-0017-backend-on-dotnet.md); more in **[`apps/server/README.md`](../apps/server/README.md)**):

```powershell
dotnet build apps/server/Weir.slnx -warnaserror
dotnet test apps/server/Weir.slnx
```

Run it:

```powershell
.\scripts\dev-backend.ps1
```

That loads **`.env`**, defaults **`WEIR_HOME`** to **`.local-dev-home`** in the repository, serves **`apps/web/dist`** when it has been built, and runs **`dotnet watch run`** on **`apps/server/src/Weir.Host`** bound to the API host and port in **`scripts/dev-ports.json`**. By hand:

```powershell
$env:WEIR_SESSION_SECRET = "<long random>"
$env:WEIR_HOME = "$PWD\.local-dev-home"
# $env:WEIR_CORS_ORIGINS = "http://127.0.0.1:8782"
dotnet run --project apps/server/src/Weir.Host -- --host 127.0.0.1 --port 18788
```

Forgot the local admin password? **`.\scripts\dev-reset-auth.ps1 --list`** shows the accounts and **`--yes`** clears users and sessions so **`/setup`** works again.

## Web app

```powershell
cd apps/web
npm ci
npm run dev
```

**`npm run dev`** stops this worktree's previous dev API (by the PID it recorded) and anything listening on the dev web port from **`scripts/dev-ports.json`**, then starts the .NET server (**`dotnet watch run`**) and Vite together (see **`apps/web/scripts/run-dev-stack.mjs`**). Use **`npm run dev:quick`** only when you are sure those ports are already free.

The Vite dev server and **`vite preview`** use **[`scripts/dev-ports.json`](../scripts/dev-ports.json)**. See **[`docs/ports.md`](ports.md)**. To override temporarily, set **`VITE_DEV_API_PROXY_TARGET`** and **`WEIR_DEV_API_PORT`** together.

### Simulation mode (web app without a server)

```powershell
cd apps/web
npm run dev:sim
```

Starts Vite against a small mock of the Weir API (**`apps/web/dev-sim/`**), so the real screens can be built, reviewed and screenshotted with files flowing through them: no .NET server, no account and no media. It prints the URL to open, on the first free port from 8790 (browse it as **`http://localhost:<port>`**; set **`SIM_PORT`** to choose one). It is a development tool only: nothing in the app imports it, it is not part of `npm run build`, and it refuses to start with `NODE_ENV=production`. It writes nothing to disk, never talks to a real Weir, and does not use the dev API port.

What it simulates, in memory for as long as it runs:

- **A signed-in admin** on a machine called MEDIA-PC, with setup finished. Signing out and in again works, and the theme choice is remembered.
- **Live work.** Files turn up in four workflows (Movies, TV, Kids with no media manager, and 4K Movies linked to a second Radarr), are held until they stop changing (sometimes with a media manager still importing them), wait for a free slot, then go through the steps a real pass reports: checking, planning, writing, verifying, handing back. Each finishes with a saved size, a hand-back that the media manager takes a few seconds later, and entries on the Activity page and in the Dashboard's Activity panel. Now and then the rules reject a file (no English audio) or a pass fails, and the file waits under "needs you"; a new session already has files waiting for each reason, including one held because Weir cannot open it and one skipped by a path rule. A library clean also runs now and then. A new session opens with a few hours of history already behind it.
- **Folder chains and connections.** Movies, TV and Kids read as in sync; 4K Movies is ready but not verified, because its manager does not say where its downloads land. Every media manager is asked again about once a minute (the server's heartbeat), so how long ago each answered keeps changing, and a manager that stops answering turns its workflows' folder chains into a fix to make; a download client is asked only when someone tests it or Weir really calls it. Each connection also says how long its last call took and when Weir last talked to it (`last_answer_ms`, `last_used_at`), and the 4K manager sometimes answers slowly as well as not at all.
- **The controls.** Pause and Resume (including a timed pause), "Files at once" and each workflow's own limit are honoured. Process again, Process all again, Remove from Activity, Move to top and cancelling a queued job act on the simulated files. The Everything / New downloads / Library cleaning filter is the page's own, except that the Activity feed is narrowed by `module` (`processing` for new downloads, `library` for library cleaning) as the server does.
- **Live updates the way the app gets them.** The same Activity stream the app uses (`GET /api/v1/activity/stream`): an `activity.latest` frame whenever something is written, a `processing.progress` frame about once a second with every running file's step and percent, and a `connection.activity` frame whenever Weir calls a media manager or download client (a test, the managers' heartbeat, a folder-chain read, a hand-off, a new download's queue lookup, a library scan) or a manager calls Weir (it takes a hand-back), so the rows on Live and System light as they do against a real server.
- **The computer and the scheduled tasks.** `GET /api/v1/system/stats`, `/system/overview` and `/system/tasks`, with `system.stats` (every second), `system.tasks` (when a task starts or ends) and `system.log` (each new warning or error) frames on the same stream. `GET /api/v1/system/log` is System › Logs: the simulation's Weir-level events, its jobs (failed ones too) and server lines, with the counts and cursor paging the server gives, and a failed pass writes a server line with its exception. The machine follows the engine: the processor, the tools' share of it and the processing read and write rates rise with the files being written and how fast they go, memory drifts slowly, and each drive's free space falls as cleaned copies are written and returns when a media manager takes one. The last ten minutes are already filled in when the session opens. The tasks are a scan and a library clean for each workflow, Weir's clean-ups, the configuration backup check, artwork lookups and the housekeeping, each with its last result and its next run from the engine's own timers; a running one shows as running.
- **Settings and System.** Every read answers in the shape of **`apps/web/openapi/weir-openapi.json`**. Saves, creates and deletes (workflows, rule sets, media managers, download clients, notification channels, performance and the rest) are kept in memory and read back. An operation the contract describes that has no hand-written answer gets the contract's own empty answer, and the console says so once. A path the contract does not describe is refused and logged.

Only fictional and public-domain titles are used. Environment variables: **`SIM_SPEED`** (how many times faster than normal the work runs; default 1), **`SIM_SEED`** (replays a session: the same files, in the same order, with the same fates) **`SIM_PORT`**, and **`SIM_SCENARIO`**: `busy` (the default) is the pace above, with the 4K manager answering slowly for a minute and twenty seconds and then dropping out for a minute and a half, every ten minutes; `quiet` has little happening, nothing waiting on a person and a flat computer, to see the empty screens; `trouble` has many files waiting on a person, Sonarr not answering (so the TV workflow needs a fix) and the 4K manager slow now and then, with bursts of other work on the processor, memory nearly full, drive D: below the free space its workflows ask Weir to keep, a configuration backup that keeps failing, and warnings and errors in the log. Stop it with Ctrl+C. If you started it in the background, stop that process by the PID you recorded, along with its Vite child.

The mock has its own tests, which `npm run test` runs: the state machine in **`dev-sim/engine/`**, that every read answers in the contract's shape, and that every route it has exists in the contract.

### API contract and generated types (OpenAPI)

**`apps/web/openapi/weir-openapi.json`** is the committed API contract. The server embeds it at build time and serves it at **`/openapi.json`** (pruned to the operations it maps, with its own version), and the web app's TypeScript types are generated from it:

```powershell
cd apps/web
npm run api:types:generate   # regenerate src/lib/api/generated/openapi-types.ts
npm run api:types:check      # CI: fails when the generated types are stale
```

When you add or change an endpoint, edit **`weir-openapi.json`** in the same change (it is maintained by hand), regenerate the types, and keep **`OpenApiDocumentParityTests`** in **`apps/server/tests/Weir.Api.Tests`** passing.

## Weir home (product paths)

On-disk runtime defaults must **not** be tied to “the Git clone directory” or the process current working directory.

- **`WEIR_HOME`** (optional): explicit absolute root for product-owned data.
- **Default when unset:**
  - **Windows:** `%PROGRAMDATA%\Weir` (normally `C:\ProgramData\Weir`)
  - **Linux/macOS:** `$XDG_DATA_HOME/weir`, or `~/.local/share/weir`

The default SQLite file is **`{WEIR_HOME}/data/weir.sqlite3`** unless **`WEIR_DB_PATH`** overrides.

**Linux containers:** set `WEIR_HOME` to a volume mount (e.g. `/var/lib/weir`) so data survives restarts.

## CI validation

The **`CI`** workflow (`.github/workflows/ci.yml`) is path-aware: a job runs only when the paths it
cares about changed (a manual run always runs everything). Its jobs:

1. **`repo-checks`**: the agent documentation map, the GitHub Actions pin check, and the release/CI
   gate rules.
2. **`server-linux`**: **`dotnet build -warnaserror`**, **`dotnet test`** and a NuGet vulnerability
   scan for **`apps/server`** on Linux.
3. **`server-windows`**: the same server build and tests on Windows (always runs on a push to `main`).
4. **`web-dist`**: builds the production web app once, for `e2e-smoke` and `contract` to serve.
5. **`web`**: **`npm ci`**, **`api:types:check`**, lint, format and unit tests in **`apps/web`**; the
   dead-code guard; an `npm audit` on pull requests.
6. **`e2e-smoke`**: Playwright against the real .NET server serving the built web app
   (**`WEIR_E2E=1`**, **`WEIR_HOME`** on a temp dir, from repo-root **`tests/e2e/weir/`**, same as
   local optional E2E below); pull requests only.
7. **`contract`**: the contract suite against the .NET server, one job per area in
   [`tests/contract/areas.json`](../tests/contract/areas.json), in parallel.
8. **`tray`** (always runs on a push to `main`): the tray's unit tests.
9. **`packaging`**: the Docker and Windows package smokes (`.github/workflows/ci-packaging.yml`) —
   builds the image, runs it and the live packaged audit, and runs the Velopack build and
   **`scripts/smoke-windows-package.ps1`**.
10. **`ci-passed`**: the verdict. It passes only when every job that was due for the change passed and
    every other job was skipped — this is the one check `main`'s ruleset requires.

Pushing a SemVer tag **`v*`** (`vX.Y.Z`, or `vX.Y.Z-rc.N` for a release candidate) runs the **`Release`** workflow. It does not repeat these tests: it
refuses to publish unless `CI` already passed (`ci-passed`) on the tagged commit, then builds, tests
and publishes the release artefacts — see **[`docs/release.md`](release.md)**.

## Contract suite and E2E (local)

Both are Python test runners. Install their locked dependencies once:

```powershell
python -m pip install --require-hashes -r tests/requirements.txt
python -m playwright install chromium
```

Build the server and the web shell, then run either:

```powershell
dotnet build apps/server/Weir.slnx
cd apps/web
npm ci
npm run build
cd ../..
python -m pytest tests/contract -q
$env:WEIR_E2E = "1"
$env:WEIR_SESSION_SECRET = "local-dev-secret-at-least-32-characters-long"
python -m pytest tests/e2e/weir -q --tb=short
```

The contract suite is described in **[`tests/contract/README.md`](../tests/contract/README.md)**. For E2E, **`WEIR_E2E_HOME`** gives a fixed data directory (otherwise a temp directory is used), **`WEIR_E2E_SERVER_EXE`** runs a published server instead of **`dotnet run --no-build`**, and **`WEIR_E2E_LEDGER`** overrides the leftover-server ledger (see **`tests/e2e/weir/conftest.py`**).

## Split-origin production

If the static site and API are on **different origins**:

- Use **HTTPS** everywhere.
- Set **`WEIR_CORS_ORIGINS`** (and **`WEIR_TRUSTED_BROWSER_ORIGINS`** if stricter POST checks) to the real web origin.
- Session cookies typically need **`SameSite=None; Secure`** on the API for credentialed cross-origin `fetch` when not using a dev proxy.

## Troubleshooting (local dev)

1. **`npm` / `npm run dev` fails**  
   Install **Node.js 24** and open a **new** terminal. From repo root: **`.\scripts\dev-web.ps1`**.

2. **`dotnet` not found, or the API exits during startup**  
   Install the **.NET 10 SDK** and open a new terminal. A build error, a missing **`WEIR_SESSION_SECRET`** or an unwritable **`WEIR_HOME`** stops the server; the reason is in the same terminal.

3. **`npm run dev` starts but login/setup is broken**  
   Use the **Vite proxy** (same origin); do not set **`VITE_API_BASE_URL`** unless you intend split-origin dev.

4. **“Cannot reach the API” vs HTTP 503**  
   **`GET /health`** on the API port (**`scripts/dev-ports.json`**) returns **200** once the server process is up, and **`GET /ready`** reports **`ready: true`** once its database is open. **`/api/v1`** still needs **`WEIR_SESSION_SECRET`** and a **writable** database path under **`WEIR_HOME`**. The web shell treats **network errors** (no TCP response) separately from **HTTP 503** from a live API (see **`apps/web`** error guards + **`ApiEntryError`**).

5. **"Weir cannot start: ... schema"**  
   The server upgrades a database at any earlier schema revision it knows and refuses anything else (a file with no schema, or a revision it does not recognise) rather than guess. Point **`WEIR_HOME`** (or **`WEIR_DB_PATH`**) at a fresh folder for a clean database.

6. **Port already in use**  
   See **`scripts/dev-ports.json`**. **`WEIR_DEV_API_PORT`** + **`VITE_DEV_API_PROXY_TARGET`** can override for one session.

7. **Two dev windows, or the server alone**  
   **`.\scripts\dev.ps1`** (launcher only — preflight warns if `.env`, the session secret or `dotnet` are missing) opens **`dev-backend.ps1`** and **`dev-web.ps1`** in separate windows with separate logs. **`npm run dev`** does not replace this: it always starts the API and Vite together, interleaved in one terminal, from `apps/web`, and cannot run the server on its own. **`dev-backend.ps1`** by itself is also how **`verify-local.ps1`** and manual API testing (curl, Postman, a `WEIR_HOME` you want to inspect between requests) point at a server that keeps running independently of any web tooling. The PowerShell trio therefore stays alongside `npm run dev` rather than being folded into it. Full check: **`.\scripts\verify-local.ps1`** with the API running.

## All-in-one Docker

For a **single-container** runtime (the .NET server, the production web app, ffmpeg and mkvmerge, with SQLite on a volume), see **`docker/README.md`** and root **`compose.yaml`**. Build locally with `docker build -t weir:local .`.

For maintainers without working local Docker, use GitHub-hosted validation:

```powershell
.\scripts\verify-docker-remote.ps1
```

The release workflow also builds, publishes, verifies, and smoke-tests Docker on GitHub-hosted runners.

## Visual shell

The **source of truth** for the product UI is **`apps/web`**. How page content is laid out is in [`design/content-language.md`](design/content-language.md).
