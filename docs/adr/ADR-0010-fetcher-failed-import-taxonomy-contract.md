# ADR-0010: Fetcher failed-import taxonomy and operator contract

## Status

Accepted — documents behavior implemented in `mediamop.modules.arr_failed_import` and Fetcher queue attention / cleanup drives.

**Fetcher failed-imports (taxonomy + policy contract):** **Complete — subject to live prod testing/signoff** on real queue wording and *arr versions only. Internal contract questions are settled here; upstream queue execution scope is ADR-0011 (same completion framing).

## Context

Sonarr/Radarr download queue rows expose human-readable status text. MediaMop classifies that text into a small enum for **policy and automation**. The classifier can return **more values** than there are **persisted operator settings fields**, by design.

## Decision

### Full runtime taxonomy (classifier)

Defined in `mediamop.modules.arr_failed_import.classification.FailedImportOutcome`:

| Value | Meaning |
| --- | --- |
| `QUALITY` | Rejected as not an upgrade / quality cutoff (wording differs Sonarr vs Radarr). |
| `UNMATCHED` | Manual import required. |
| `SAMPLE_RELEASE` | Sample or junk release. |
| `CORRUPT` | Corrupt file / integrity failure. |
| `DOWNLOAD_FAILED` | Download client / transport failure. |
| `IMPORT_FAILED` | Generic import failure phrases. |
| `PENDING_WAITING` | **Non-terminal** for cleanup: waiting to import / pending only (no terminal rejection matched first). |
| `UNKNOWN` | **Non-terminal** for cleanup: empty blob or no rule matched. |

Classification precedence is in `classify_failed_import_message_for_media`: terminal needle order first, then pending phrases, else `UNKNOWN`.

There are **no separate legacy names** in code paths: one enum, one classifier.

### Policy-backed terminal outcomes (operator-configurable)

Only the **six** outcomes in `policy._TERMINAL_OUTCOME_TO_KEY` map to `FailedImportCleanupPolicyKey` and persisted SQLite/env fields:

`QUALITY`, `UNMATCHED`, `SAMPLE_RELEASE`, `CORRUPT`, `DOWNLOAD_FAILED`, `IMPORT_FAILED`.

`PENDING_WAITING` and `UNKNOWN` map to **no** policy key (`cleanup_policy_key_for_outcome` returns `None`). Operators cannot assign remove/blocklist to them; Fetcher does not treat them as cleanup targets.

### Visible settings rows

The Fetcher **Failed imports** UI exposes **six** rows per axis (TV / movies), one per `FailedImportCleanupPolicyKey` / handling field. That list is **exclusive and complete** for policy-backed classes. It is **not** a row per enum member: two enum members deliberately have no row.

### Needs attention

`failed_import_queue_attention_service.count_classified_failed_import_queue_rows` uses `decide_failed_import_cleanup_eligibility`. A row increments the count only when `cleanup_eligible` is true: terminal outcome with a policy key **and** configured action other than `leave_alone`. Pending/unknown rows never count.

### Action execution

`FailedImportQueueHandlingAction` maps to Sonarr/Radarr `DELETE /api/v3/queue/{id}` query flags via `queue_delete_flags_for_action` in `queue_action.py`:

| Action | removeFromClient | blocklist |
| --- | --- | --- |
| `leave_alone` | (no DELETE) | (no DELETE) |
| `remove_only` | true | false |
| `blocklist_only` | false | true |
| `remove_and_blocklist` | true | true |

Sonarr and Radarr each run the same policy seam with `movies=False` vs `movies=True` for classification only; HTTP clients and job kinds are split per ADR-0009.

## Consequences

- Documentation and UI copy must **not** claim “eight outcomes, eight rows” or “enum 1:1 with rows.” Correct phrasing: **eight classifier outcomes; six policy-backed terminal outcomes; six operator rows.**

- Adding a new terminal class requires: enum value, needle rules, `_TERMINAL_OUTCOME_TO_KEY` entry, policy field, API schema, UI row, and tests.

## Live verification

Correctness of **phrase lists** vs real Sonarr/Radarr versions in the field remains empirical: new status wording can land in `UNKNOWN` until needles are extended. That does not invalidate this ADR’s structural contract.

## See also

- **ADR-0011** — upstream Sonarr/Radarr queue `DELETE` contract, `removeFromClient` / `blocklist` semantics, pending-row caveat, and live checklist.
