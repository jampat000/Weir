# Release Governance

This is the canonical governance checklist for keeping Weir releases controlled and repeatable.

## GitHub repository controls

- `main` is protected by the active `Owner-Only Main Protection` ruleset.
- Direct deletion and force-pushes to `main` are blocked.
- Pull requests into `main` require conversation resolution.
- Code-owner review is required by the ruleset. `.github/CODEOWNERS` owns the full tree.
- The one required status check for `main` is `ci-passed`, CI's verdict (`.github/workflows/ci.yml`,
  `scripts/ci-passed.mjs`): every job the change made due passed, and every other job was skipped by path
  filtering. The individual jobs (`server-linux`, `server-windows`, `web`, `web-dist`, `e2e-smoke`, the
  `contract (<area>)` legs, `tray`, `docker-smoke`, `windows-package-smoke`, `repo-checks`) are judged by
  it, so none of them is a required check of its own.
- The repo Wiki is disabled. Public docs live in the repository.
- Issues are enabled and use structured templates.
- Releases are tag-driven from `v*` tags; a prerelease tag (`v*-*`) runs no release.

## Before every release

1. Confirm the working tree is clean.
2. Confirm `main` is up to date with `origin/main`.
3. Confirm `.github/workflows/ci.yml` still has the `ci-passed` job the ruleset requires (`node scripts/check-release-workflow-gates.mjs` checks it).
4. Confirm Dependabot has no stale action-runtime holds that conflict with the workflow pins.
5. Confirm open issues tagged `priority: critical` or `priority: high` are either fixed, intentionally deferred, or not release-blocking.
6. Create `docs/release-notes/vX.Y.Z.md` from `docs/release-notes/TEMPLATE.md` with plain-language user-facing notes.
7. Run the release path from `docs/release.md`.

## After every release

1. Confirm the GitHub Release exists for the pushed tag.
2. Confirm `Weir-win-Setup.exe` is attached to the release.
3. Confirm the published release body is plain-language and matches the approved `docs/release-notes/vX.Y.Z.md` content.
4. Confirm the release notes/install guidance names the attached `Weir-win-Setup.exe` installer and explains any one-time upgrade requirement for older installs.
5. Confirm the GHCR image exists for both `vX.Y.Z` and `latest` (`latest` moves only after the version's image passed its smoke and the release was published).
6. Confirm the release workflow completed `ci-passed`, `validate`, `windows-smoke`, `docker-candidate`, `docker-arm64` and `publish`.
7. Download `weir-docker-release-candidate-audit` and confirm its summary has
   no console warnings, console errors, page errors, failed requests, or bad responses;
   confirm `pass-through-proof.json` reports a completed job, byte-identical output,
   and successful watched-source cleanup.
8. Open a follow-up issue for any manual smoke-test failure.
