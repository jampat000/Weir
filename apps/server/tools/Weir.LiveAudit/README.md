# Weir packaged live audit

A Playwright for .NET walk through every screen of a real, installed Weir: the Docker candidate image or the
Windows package. It checks the public pages and the signed-in read surface, the shell at several widths, Settings,
Processing (a file taken through a pass-through lifecycle), History, Activity and the media manager and notification
screens, and writes screenshots plus a machine-readable `summary.json`.

It does not start a server. Point it at one that is already running:

```bash
dotnet build apps/server/tools/Weir.LiveAudit -c Release
pwsh apps/server/tools/Weir.LiveAudit/bin/Release/net10.0/playwright.ps1 install chromium   # once
WEIR_LIVE_BASE_URL=http://localhost:9347 dotnet run --project apps/server/tools/Weir.LiveAudit -c Release
```

Run it from the repository root: the artifacts folder and the expected version (read from
`apps/server/Directory.Build.props`) are found from there. Exit code 0 means every step passed, 1 that a step or the
browser failed (`failure.png` is written next to the screenshots), 2 that `WEIR_LIVE_BASE_URL` is not set.

CI runs it in `docker-smoke` (`.github/workflows/ci-packaging.yml`) and the release workflow runs it against the
unpushed release candidate (`.github/workflows/release.yml`).

| Variable | Meaning |
| --- | --- |
| `WEIR_LIVE_BASE_URL` | The server to audit (required). |
| `WEIR_LIVE_EXPECTED_VERSION` | The version it must report. Default: `WeirVersion` in `apps/server/Directory.Build.props`. |
| `WEIR_LIVE_E2E_ARTIFACTS` | Where screenshots and `summary.json` go. Default: `artifacts/live-packaged-e2e`. |
| `WEIR_LIVE_E2E_FIXTURE_HOST_ROOT` / `WEIR_LIVE_E2E_FIXTURE_SERVER_ROOT` | The folder the audit writes its generated media to, as the audit sees it and as the server sees it (they differ when the server runs in a container). |
| `WEIR_LIVE_E2E_FFMPEG` | The `ffmpeg` that generates that media. Default: `ffmpeg`. |
| `WEIR_LIVE_E2E_DOCKER_CONTAINER` | The container to read the one-time setup code from, for a server whose peer is not loopback (Docker). |
| `WEIR_LIVE_E2E_DOCKER_HOME` | That container's `WEIR_HOME`. Default: `/data/weir`. |
| `WEIR_LIVE_E2E_USER` / `WEIR_LIVE_E2E_PASSWORD` | The admin account the audit creates. Defaults are fixed audit values. |
