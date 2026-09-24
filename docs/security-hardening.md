# Security Hardening

This checklist defines the current practical hardening baseline for Weir.

## Authentication and setup

- First-run bootstrap is only available when no admin user exists.
- Bootstrap from a non-loopback peer needs the one-time setup code Weir logs and writes to
  `WEIR_HOME/setup-code` at start-up; a loopback peer (the tray's `127.0.0.1`) does not.
- Passwords shorter than 8 characters must be blocked by both frontend and backend validation.
- Login and bootstrap routes are rate-limited per IP; login also backs off per account after 5
  failures in 15 minutes, independent of which addresses the guesses came from.
- Authenticated state-changing browser requests require CSRF protection.
- Session cookies are HTTP-only.
- Secure cookies should be enabled when deployed behind HTTPS.
- Existing users must persist across restarts and upgrades.
- Requests are only answered for a `Host` Weir recognises (see
  [Reverse Proxy: Host header allow-list](https://github.com/jampat000/Weir/blob/main/docs-site/docs/deployment/reverse-proxy.md)) — set `WEIR_ALLOWED_HOSTS` for a reverse-proxy domain.
- Install-level routes (configuration bundle, backups, updates, the directory browser, media
  manager and notification channel credentials, history reset) require the admin role.

## Secrets and private data

- Real `.env` files must never be committed.
- Runtime SQLite databases must never be committed.
- Logs, backups, media paths, API keys, provider tokens, and session secrets must never be committed.
- Public issue logs must redact secrets, private hostnames, and private filesystem paths.
- Docker can generate a persistent session secret when one is not provided.
- `WEIR_SESSION_SECRET` signs sessions and CSRF tokens; `WEIR_CREDENTIALS_SECRET` encrypts saved provider
  credentials. Keep them separate.
- `WEIR_METRICS_BEARER_TOKEN` can gate machine access to `/metrics` without requiring an operator browser session.
- To rotate `WEIR_CREDENTIALS_SECRET`, set the new value as `WEIR_CREDENTIALS_SECRET`, add the old value to
  `WEIR_PREVIOUS_CREDENTIALS_SECRETS`, restart Weir, then re-save every media manager
  connection (Settings › Media managers) and the TMDb metadata provider key (Settings › Rules). After every saved credential has been re-written with the new value, remove the old value from
  `WEIR_PREVIOUS_CREDENTIALS_SECRETS` and restart again.

## Repository and dependency controls

- `main` is protected by GitHub rules.
- The required check is `ci-passed` (the `CI` workflow's verdict job; see `docs/local-development.md`).
- Dependabot is enabled for NuGet (`apps/server`, `apps/tray`), npm (`apps/web`, `docs-site`), GitHub Actions, and the Python test-runner packages in `tests/requirements.txt`.
- CodeQL code scanning (C# and JavaScript/TypeScript) runs on `main`, pull requests to `main`, weekly schedule, and manual dispatch.
- Security vulnerabilities are reported privately through `SECURITY.md`.
- Public issues are not used for unpatched vulnerabilities.
- CI runs a NuGet vulnerability scan (`node scripts/check-dotnet-vulnerabilities.mjs apps/server/Weir.slnx`, failing on High or Critical) and `npm audit` in addition to CodeQL and standard test gates.

## Release controls

- Releases are built from tagged source.
- Windows and Docker artifacts are produced by GitHub Actions.
- Local Docker Desktop is not required to validate Docker packaging.
- Release artifacts remain under AGPL-3.0-or-later.

## Ongoing checks

Run this list before any major release:

1. Confirm no secrets or runtime files are staged.
2. Confirm dependency audit jobs pass.
3. Confirm CodeQL has no open high-confidence security findings.
4. Confirm auth setup, login, logout, and password validation smoke tests pass.
5. Confirm backup files do not expose secrets in public docs, logs, or screenshots.
6. Confirm History and System › Logs do not expose tokens or internal implementation details to normal users.
