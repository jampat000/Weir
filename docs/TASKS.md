# MediaMop — repo-local task tracking

Single canonical backlog for shipped milestones and the next honest slice of work. ADRs remain the authority for locked architecture; this file is for **what is done vs what is next**.

## Completed (Refiner and related suite)

- [x] Refiner ownership vs upstream blocking split verified and correct in domain code.
- [x] Refiner anchor-weighted queue matching verified and correct.
- [x] Refiner per-file remux family shipped (`refiner.file.remux_pass.v1`) with path model and safe defaults.
- [x] Refiner remux pass operator visibility (activity / structured results) shipped for current scope.
- [x] Refiner in-process worker concurrency surfaced to operators (`runtime-settings` + env honesty).
- [x] Refiner path model shipped (persisted watched / work / output; containment and lifecycle rules locked).
- [x] Refiner watched-folder scanning family shipped: `refiner.watched_folder.remux_scan_dispatch.v1` (manual trigger, media scan, gate-equivalent ownership/blocking, optional remux enqueue, duplicate guard, activity summary, Refiner page UI + POST enqueue API).

## Active / next

- [ ] Next milestone: choose from product priorities (no mandatory open item for the watched-folder scan family). Likely follow-ups when needed: richer Activity rendering for scan summaries if plain `detail` text is not enough; `refiner_jobs` inspection/recovery HTTP when the product is ready to ship it (per ADR-0007).
