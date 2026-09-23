# Weir diagnostics contract

Weir is a single-node media processing app that can move, write, and delete user media. Diagnostics must be useful to an operator without exposing secrets or hiding unsafe states.

## Structured events

Operational events should use the shared diagnostics vocabulary in `Weir.Core/Observability` (`Diagnostics` and `DiagnosticEvent`).

Events should include the fields that apply:

- `module`: `processing`, `auth`, `system`, or shared service name.
- `provider`: upstream system such as Sonarr, Radarr, or Deluno.
- `media_scope`: `tv`, `movies`, or both when the operation spans both.
- `action`: scan, preview, remux, pass-through, reject, cleanup, hand-back, import, connection test, schedule run, or update.
- `trigger`: manual, scheduled, webhook, folder change, worker, startup, retry, or system.
- `result`: success, skipped, retrying, warning, failed, or running.
- `counts`: scanned, matched, processed, deleted, skipped, failed, retried, written, or tracks removed.
- `correlation_id`: request, job, or run identifier when the workflow spans multiple steps.
- `reason` and `next_action`: plain operator-readable explanation when an operation fails, skips, or needs attention.

`DiagnosticEvent.AsSafeDict` writes only the fields that are set and redacts secret-looking values.

## Severity rules

- `debug`: routine diagnostics, commands, normal probe details.
- `info`: successful user-visible work and normal scheduled work.
- `warning`: recoverable issue needing operator awareness.
- `error`: failed job/action or unrecoverable failure.

Normal operation must not be warning-level output. `Diagnostics.SeverityForResult` derives severity from `result`: `failed` is `error`, `warning` and `retrying` are `warning`, and everything else is `info`.

## Correlation

Long-running work must preserve a correlation identifier across API requests, durable queue jobs, worker execution, provider calls, activity rows, and logs. A job id, run id, or request id is acceptable if it is stable for that workflow.

## Operator wording

Messages must explain what happened and what the user should do next. Do not expose raw exceptions as the only message. Do not expose secrets, tokens, passwords, API keys, cookie values, or signed session data.

## Runtime truthfulness

Readiness, metrics, the Processing and History screens, and the Settings and System pages must reflect real runtime behaviour. A disabled worker, unavailable dependency, queued but unprocessed job, or skipped deletion must be shown as degraded/skipped, not successful.

Success totals on Processing and History must be derived from explicit terminal outcome components. Do not calculate a success total from queued jobs, scanned files, attempted work, or broad completed queue rows unless that queue row itself proves finalized work. `MetricsTruth` in `Weir.Core/Observability` refuses negative counts when a total is built.
