# Issue Triage

Weir issues should stay practical and reproducible. Every issue needs a clear user impact, an affected area, and a next action.

## Labels

- `type: bug` - something is broken or behaves incorrectly.
- `type: enhancement` - a new feature or workflow improvement.
- `type: docs` - documentation, screenshots, release notes, or support text.
- `area: processing` - libraries, rules, the remux pipeline, jobs, History and Library.
- `area: settings` - settings, security, logs, backup, and support screens.
- `area: logs` - runtime logs, diagnostics, retention, and observability.
- `area: web` - shared web shell, responsive layout, accessibility, and frontend delivery.
- `area: setup` - first-run setup and local development startup.
- `area: installer` - Windows setup, startup behavior, tray app, or upgrade flow.
- `area: docker` - image build, runtime config, ports, volumes, or container startup.
- `area: docs` - documentation site, build, dependencies, and publishing.
- `area: ci` - GitHub Actions, release automation, packaging, or dependency automation.
- `priority: critical` - data loss, security exposure, or release-blocking install failure.
- `priority: high` - core workflow broken with no reasonable workaround.
- `priority: normal` - important but not release-blocking.
- `priority: low` - small polish, cleanup, or nice-to-have.
- `status: needs triage` - newly opened and not yet confirmed.
- `status: blocked` - waiting on external information or a decision.
- `status: ready` - scoped enough to implement.

### Legacy labels

These still exist on the repository and on older issues. Do not put them on new issues.

- `area: home` - the Home screen, which Processing replaced as the first screen. Use `area: processing`. <!-- retired-ui: history -->
- `area: activity` - the Activity page, whose events are now in System › Logs. Use `area: logs`. <!-- retired-ui: history -->
- `area: pruner` - Pruner moved to Deluno and its tables were dropped by migration `0036_drop_pruner_tables`.

## Triage rules

1. Confirm the issue has version, install type, affected area, reproduction steps, expected result, and actual result.
2. Remove secrets, tokens, and private filesystem paths from logs before discussing them publicly.
3. Assign exactly one `type:*` label and at least one `area:*` label.
4. Add one `priority:*` label after impact is understood.
5. Close issues only when the fix is merged, intentionally declined, or superseded by a better issue.

## Backlog references

- Release governance: [`release-governance.md`](release-governance.md)
- Windows and Docker smoke checks: [`smoke-checklists.md`](smoke-checklists.md)
- Security hardening: [`security-hardening.md`](security-hardening.md)
- UX polish baseline: [`ux-polish.md`](ux-polish.md)

