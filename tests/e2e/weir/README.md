# Weir E2E smoke

Playwright tests that drive the built web app against a real Weir server (sign-in, navigation, the seeded
Settings and Activity screens, a visual smoke check). The server is started from `apps/server` by
`conftest.py`; the tests only talk to it through the browser and plain SQL on its stopped database.

> **Temporary Python.** Weir is a .NET product; these tests are Python only because they were written before the port. CI runs them as the `e2e-smoke` job in `.github/workflows/ci.yml`, and the release workflow runs them again before publishing. Issue #892 ports them to .NET and then deletes this folder.

Run from the repository root, after building the server and `apps/web`: `python -m pytest tests/e2e/weir -q` (see `CONTRIBUTING.md`).
