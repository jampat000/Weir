# ADR-0011: *arr* failed-import queue delete — upstream contract vs MediaMop

## Status

**ADR:** Accepted — source review of Sonarr/Radarr `develop` branches as of audit date; complements ADR-0010 (internal taxonomy).

**Failed-imports (this contract):** **Complete — subject to live prod testing/signoff.** Source and in-repo audits are not a substitute for running against your installed Sonarr/Radarr versions, real queue data, and real download clients.

### What live signoff still covers

- Real queue / status message wording on your stack (classification needles vs actual text).
- Real **tracked vs pending** queue behavior where your items hit the pending branch.
- Real **download-client** reaction to remove / blocklist / combined actions.
- Regression-style confirmation after upgrades or config changes on **your** stack.

### What is already settled (not part of this proviso)

- Failed-import **architecture** and in-process wiring.
- Operator UI for policy, needs attention, jobs, and manual passes (as shipped).
- **Taxonomy** (8 classifier outcomes, 6 policy-backed terminals) and policy mapping — ADR-0010.
- **API usage inside MediaMop** for queue `DELETE` flags vs upstream `QueueController` — this ADR.

## Primary upstream evidence

### Sonarr

- **File:** `Sonarr.Api.V3/Queue/QueueController.cs` on `Sonarr/Sonarr` (`develop`).
- **Route:** `[RestDeleteById]` → `DELETE /api/v3/queue/{id}` (v3 controller; same pattern Radarr copies).
- **Signature:** `RemoveAction(int id, bool removeFromClient = true, bool blocklist = false, bool skipRedownload = false, bool changeCategory = false)`.
- **Tracked downloads:** `Remove(TrackedDownload trackedDownload, bool removeFromClient, bool blocklist, …)`:
  - If `removeFromClient`: `downloadClient.RemoveItem(...)`.
  - If `blocklist`: `_failedDownloadService.MarkAsFailed(trackedDownload, skipRedownload)`.
  - If **none** of `removeFromClient`, `blocklist`, `changeCategory`: `_ignoredDownloadService.IgnoreDownload(trackedDownload)` (MediaMop never issues this combination for operator actions).

### Radarr

- **File:** `Radarr.Api.V3/Queue/QueueController.cs` on `Radarr/Radarr` (`develop`).
- **Same** `RemoveAction` signature and **same** private `Remove(TrackedDownload, …)` structure as Sonarr above.

### Pending queue items (both apps)

- If `FindPendingQueueItem(id)` matches, upstream calls `Remove(pendingRelease, blocklist)` only — **no `removeFromClient` parameter**. Blocklist still uses `_blocklistService.Block(...)` when `blocklist` is true; pending row is removed via `_pendingReleaseService.RemovePendingQueueItems`.
- **Implication:** For IDs that resolve to **pending** queue entries, the `removeFromClient` query flag is **not consulted** by upstream. MediaMop still sends it (harmless). Operators should live-verify behavior for pending vs tracked rows if they rely on download-client removal semantics.

### MediaMop HTTP client

- `SonarrQueueHttpClient` / `RadarrQueueHttpClient`: `DELETE {base}/api/v3/queue/{id}?removeFromClient={true|false}&blocklist={true|false}` with `X-Api-Key`.
- **Not sent:** `skipRedownload`, `changeCategory` — upstream defaults apply (`skipRedownload=false`, `changeCategory=false`). Documented choice; change only if product adds explicit controls.

## Upstream evidence table (summary)

| Topic | Sonarr evidence | Radarr evidence | Cleanuparr / comparable | MediaMop behavior | Match? | Confidence |
| --- | --- | --- | --- | --- | --- | --- |
| Queue delete endpoint | `DELETE /api/v3/queue/{id}` (`QueueController.RemoveAction`) | Identical | Ecosystem scripts/docs refer to v3 queue DELETE (e.g. community queue cleaners); Cleanuparr README is feature-level only — **no line-level API audit in this pass** | Same path | Match | **High** (source) |
| HTTP method | DELETE | DELETE | — | DELETE | Match | High |
| `removeFromClient` | Query param; default `true` in signature | Same | — | Explicit `true`/`false` per action | Match | High |
| `blocklist` | Query param; default `false` | Same | — | Explicit | Match | High |
| `remove_only` semantics | `RemoveItem` when true; no `MarkAsFailed` when blocklist false | Same | — | `(true, false)` | Match | High |
| `blocklist_only` semantics | No `RemoveItem`; `MarkAsFailed` when blocklist true | Same | — | `(false, true)` | Match | High (tracked path) |
| `remove_and_blocklist` | Both branches | Same | — | `(true, true)` | Match | High |
| Pending row semantics | `removeFromClient` ignored | Same | — | Still sends flags | **Partial** | High on upstream; MediaMop harmless |
| `skipRedownload` | Supported, default false | Same | — | Omitted (default false) | Match | High |
| Queue message / status reliance | UI and API expose `statusMessages`, status fields — wording evolves | Same | Tools in this space substring-match status text (same fragility class as MediaMop) | `sonarr_queue_item_status_message_blob` / radarr equivalent + `classification.py` needles | Aligned in approach | **Medium** for phrase stability — live / version drift |
| Known upstream bugs | — | e.g. [#10019](https://github.com/Radarr/Radarr/issues/10019) “delete without blocklist still blocklisted” — **closed** with fix `85b310c` (2024); maintainer could not repro on nightly before fix | Cleanuparr positions as queue cleaner; no commit-level comparison performed here | MediaMop passes explicit booleans; does not rely on *arr defaults for flags | Match intent | High for explicit flags; **residual** UI/API bugs always possible per version |

## Action semantics (tracked download — source-confirmed)

For **tracked** queue items (normal failed-import cleanup path):

| MediaMop action | `removeFromClient` | `blocklist` | Upstream effect (from `Remove` body) |
| --- | --- | --- | --- |
| `leave_alone` | — | — | No DELETE issued by MediaMop. |
| `remove_only` | true | false | Remove from download client; **not** `MarkAsFailed` / blocklist path from this call. |
| `blocklist_only` | false | true | **Not** `RemoveItem` on client; **yes** `MarkAsFailed` (blocklist / failed handling in *arr core). Queue row cleared from *arr’s perspective via failed flow — **download client may still hold files** until separately managed; **live-verify** per client. |
| `remove_and_blocklist` | true | true | Remove from client **and** `MarkAsFailed`. |

## Phrase / message contract

- MediaMop flattens `statusMessages` (+ status-like fields) into one blob, then substring-matches normalized text (`classification.py`). This matches how **community tooling** typically works; it is **not** a guarantee against Sonarr/Radarr copy changes or localized builds.
- **Confidence:** implementation approach is standard; **per-phrase** guarantees are **not** source-provable across all versions — only empirically extended via needles + live samples.

## Comparable tools

- **Cleanuparr:** Public materials describe automated queue cleanup for Sonarr/Radarr; this audit did **not** pull Cleanuparr implementation lines (GitHub code search unavailable without auth). **Lesson:** treat third-party tools as corroboration that the **API pattern exists in the ecosystem**, not as a formal conformance proof for MediaMop.
- **Community scripts** (e.g. `sonarr-radarr-queue-cleaner`-style repos): commonly use `removeFromClient` / `blocklist` query params on v3 queue DELETE — consistent with MediaMop.

## Mismatches requiring code changes

**None identified.** MediaMop’s mapping matches Sonarr/Radarr `QueueController` for tracked items. Residual risks are **version**, **pending-vs-tracked**, and **historical bugs** — documented here, not patched speculatively.

## Live verification checklist (minimal)

1. **Tracked item, remove only:** Queue row with known failed-import text → policy `remove_only` → run pass → confirm **blocklist empty** for that release and download client received removal (per client UI).
2. **Tracked item, blocklist only:** Same → `blocklist_only` → confirm **blocklist gains entry**, download client **not** asked to remove (verify client still has payload if applicable).
3. **Tracked item, remove + blocklist:** Confirm both.
4. **Pending item (if applicable):** Queue id that resolves to pending → confirm upstream behavior matches expectation (removeFromClient ignored); adjust operator expectations if needed.
5. **Regression guard:** `remove_only` must **never** add blocklist entry (spot-check after Radarr-style fixes).
6. **Classification:** Capture **raw** `statusMessages` JSON from Sonarr/Radarr for a real failed import → confirm blob maps to intended **six** terminal class in staging.

## Impossible without live execution

- Exact **MarkAsFailed** side effects for every indexer + download client combo.
- Future **string wording** changes in *arr UI/API without code search on new versions.
- **Cleanuparr** line-by-line parity (not fetched).

## See also

- ADR-0010 — internal taxonomy and six operator rows.
- `mediamop.modules.arr_failed_import.queue_action` — explicit `(removeFromClient, blocklist)` matrix with upstream reference in module docstring.
