# Repository scripts

## Which language

- **Node** (`.mjs`) for repository tooling and CI gates. Node is on every CI runner and every
  workstation that builds the web app, and needs no install step.
- **Python** only for Playwright and test tooling, which share `tests/requirements.txt` with the
  E2E smoke and the contract suite.
- **PowerShell** only for Windows packaging and checks that exercise the Windows package.

A new script follows these rules. A script in the wrong language is replaced when it next needs real
work, not rewritten for its own sake. Scripts are named in kebab-case.

## CI and release gates

| Script | What it does |
| --- | --- |
| `ci-passed.mjs` | CI's single verdict: every job due for the change passed, every other job was skipped (`ci-passed.test.mjs`). |
| `check-agent-docs.mjs` | Checks that the agent documentation map (`AGENTS.md` and its links) is valid. |
| `check-github-action-pins.mjs` | Fails when a workflow uses an action that is not pinned to a full commit SHA. |
| `check-release-workflow-gates.mjs` | Checks the shape of `release.yml` and `ci.yml`: only `publish` publishes, `latest` moves last, `ci-passed` judges every job. |
| `check-release-version.mjs` | Fails a release whose tag does not match `WeirVersion` and `apps/web/package.json`. |
| `verify-ci-for-release.mjs` | Makes a release prove `ci.yml`'s `ci-passed` passed on the tagged commit (`verify-ci-for-release.test.mjs`). |
| `check-dead-code.mjs` | Dead-code guard for the web app: unused exports (allowlist in `dead-code-allowlist.json`) and unstyled class names. |
| `check-dotnet-vulnerabilities.mjs` | Fails on High or Critical NuGet advisories in a .NET solution. |
| `wait-for-health.mjs` | Waits for a server's `/health`, printing a container's log if it never answers. |
| `ffmpeg-cache-key.mjs` | Prints the cache key for the Windows package's vendored FFmpeg: upstream's current build checksum. |

## Local development

| Script | What it does |
| --- | --- |
| `pre-push.mjs` | The pre-push checks `.githooks/pre-push` runs: ruff, prettier, the dead-code guard and API types drift. |
| `stop-dev-api-port.mjs` | Stops the dev API that this worktree's `npm run dev` started, and nothing else. |
| `stop-dev-web-port.mjs` | Stops the dev Vite server that this worktree's `npm run dev` started, and nothing else. |
| `dev-reset-auth.mjs` | Clears a development database's users and sessions so `/setup` works again. |
| `dev-ports.json` | The development and production ports every launcher and the Vite config read. |
| `dev.ps1`, `dev-backend.ps1`, `dev-web.ps1`, `weir-env.ps1`, `dev-reset-auth.ps1`, `verify-local.ps1` | PowerShell dev launchers and checks. `npm run dev` in `apps/web` always starts the API and Vite together and cannot run the server alone; this trio stays for the API-only case (`verify-local.ps1`, manual API testing) and for two separate windows with separate logs. See `docs/local-development.md`. |

## Windows packaging and live audits

| Script | What it does |
| --- | --- |
| `smoke-windows-package.ps1` | Starts the assembled Windows package's server and checks it end to end. |
| `verify-docker-remote.ps1` | Runs the Docker validation workflow on GitHub-hosted runners from a machine without Docker. |
| `live-packaged-e2e.py` | Full browser and API audit of a packaged server (Docker or Windows) at `WEIR_LIVE_BASE_URL`. |
| `screenshot-site.py` | Screenshots every screen in both themes and widths, with `build-contact-sheet.py` for the review page. |
| `capture-readme-screenshots.py` | Captures exactly the screenshots `README.md` publishes. |
| `generate-brand-icons.py` | Renders every raster icon from the SVG sources in `packaging/brand`. |
