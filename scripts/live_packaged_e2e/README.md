# Live packaged E2E audit

The audit modules behind `scripts/live-packaged-e2e.py`: a Playwright walk through every screen of a real,
installed Weir (the Docker candidate image or the Windows package), checking the public pages, shell,
Settings, Processing and notifications against the running product.

> **Temporary Python.** Weir is a .NET product; this audit is Python only because it was written before the port. CI runs it in the `docker-smoke` and `windows-package-smoke` jobs (`.github/workflows/ci-packaging.yml`), and the release workflow runs it against the release candidates. Issue #892 ports it to .NET and then deletes this folder and `scripts/live-packaged-e2e.py`.
