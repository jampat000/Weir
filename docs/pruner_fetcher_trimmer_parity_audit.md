# Pruner vs legacy Fetcher/Trimmer parity audit (Phase 2)

This document compares **current MediaMop Pruner** (this repo, `apps/backend/src/mediamop/modules/pruner/`) with the **legacy Fetcher/Trimmer** codebase at `C:\Users\User\Fetcher` (`app/emby_rules.py`, `app/trimmer_service.py`, `app/templates/trimmer_settings.html`). Scope is limited to **rule families**, **filter types**, and **operator flows** as requested.

## Legacy Trimmer reality (Fetcher repo)

- **Provider:** Emby/Jellyfin-style **Items API** via `EmbyClient.items_for_user(user_id=…)` (`trimmer_service.py`). Explicit **Emby user id** selection (or first user). **No Plex** in the Trimmer path.
- **Rules (from `emby_rules.evaluate_candidate`):**
  - Movies: **watched** + **user rating** (`UserData.Rating`) below `movie_watched_rating_below` (integer threshold).
  - TV: optional **delete all watched** (`tv_delete_watched`).
  - TV/Movies: **unwatched** + **age** from item `DateLastMediaAdded` / `DateCreated` / `PremiereDate` (`days_since`).
- **Filters:** Per-rule **genre** CSV; **people** phrases with **credit type** subset (`actor`, `director`, etc.) and **substring** name match (`phrase in n`).
- **Operator flow:** **Review scan** (optional full library scan on demand) → **live delete** path (`TrimmerApplyService`) with **dry-run** global toggle; deletes applied in-session from review candidates, not from a frozen cross-run snapshot.

## What MediaMop Pruner already matches (conceptual overlap)

| Area | Legacy | Current Pruner |
|------|--------|------------------|
| Per-server URL + secret | Emby URL + API key | Instance `base_url` + encrypted credentials envelope |
| User-scoped play state | `UserData.Played` / rating on Items | Jellyfin/Emby: `UserData` / `IsPlayed`; Plex (movies): `viewCount` / `lastViewedAt` on `allLeaves` |
| Watched movies removal | Candidate movies | `watched_movies_reported` preview → snapshot apply |
| Low rating on watched movies | `UserData.Rating` vs threshold | JF/Emby: `CommunityRating` vs `watched_movie_low_rating_max_jellyfin_emby_community_rating`; Plex: `audienceRating` vs `watched_movie_low_rating_max_plex_audience_rating` (separate persisted ceilings) |
| Unwatched + age | Unwatched + `days_since` from several date fields | JF/Emby: `DateCreated` + unplayed; Plex: `addedAt` + unwatched test on leaf |
| Genre narrowing | Selected genre set | Per-scope `preview_include_genres` (exact token match; JF/Emby/Plex where implemented) |
| People narrowing | Phrases + credit types, substring | Pruner: **full-name exact** match; JF/Emby `People`; Plex Role/Writer/Director tags |

## What Pruner does **intentionally** differently

1. **Preview-first + snapshot apply:** Pruner never applies deletes from a fresh scan at apply time; apply uses **only** `pruner_preview_runs.candidates_json`. Trimmer applies from the **last in-memory review** (live scan coupling).
2. **Strict provider isolation:** Jellyfin, Emby, and Plex have **separate** collector code paths and explicit unsupported strings instead of one assumed “Emby-like” model.
3. **Stricter matching:** Pruner people filters use **exact** normalized equality, not substring `phrase in name`.
4. **No credit-type picker for Pruner people filters** in the current product slice (legacy had `parse_movie_people_credit_types_csv`).
5. **Plex never-played stale / watched TV:** Still **unsupported** in Pruner (no honest equivalent on the single-pass `allLeaves` contract for those rule shapes in this architecture).
6. **Legacy Trimmer** ties to **Sonarr/Radarr** keys on apply (`apply_emby_trimmer_live_deletes`); Pruner **does not** orchestrate Arr in the apply handler shown in this audit scope.

## Legacy capabilities that still look **worth porting** (future work, not in this commit)

1. **Optional people credit-type filter** for JF/Emby (and possibly Plex if tag roles were mapped honestly) — would narrow false positives vs today’s “any People name”.
2. **Operator-configurable “rating field” documentation** in UI for Plex vs JF/Emby side-by-side when both exist on one operator’s mental model.
3. **Trimmer-style dry-run global** is partially echoed by Pruner’s `MEDIAMOP_PRUNER_APPLY_ENABLED`, but a **per-instance** “allow apply” toggle could match Trimmer’s ergonomics without breaking snapshot semantics.

## What should **not** be ported (conflicts with Pruner architecture / truth rules)

1. **Substring people matching** — too loose for safe library deletion; conflicts with Pruner’s explicit filter story.
2. **Apply-from-live-rescan** — violates snapshot-only apply and skip-if-gone semantics.
3. **Pretending Plex `audienceRating` is `CommunityRating`** — separate persisted ceilings and API fields keep the distinction honest.

## Where Pruner **supersedes** legacy behavior

- **Multi-instance, scope rows, audit trail:** `pruner_preview_runs`, activity events, per-tab caps, and API surface in `pruner_instances_api.py` replace the monolithic Trimmer settings model for operator accountability.
- **Plex missing-primary** and now **Plex movie rules** use documented leaf fields (`pruner_plex_missing_thumb_candidates.py`, `pruner_plex_movie_rule_candidates.py`) instead of unsupported stubs.

## References (legacy files read)

- `C:\Users\User\Fetcher\app\emby_rules.py` — `evaluate_candidate`, genre/people helpers, `days_since`, `emby_user_played`, `emby_rating`.
- `C:\Users\User\Fetcher\app\trimmer_service.py` — `TrimmerReviewService.build_review`, `TrimmerApplyService`, scan limits, user resolution.
- `C:\Users\User\Fetcher\app\templates\trimmer_settings.html` — connection, dry-run, schedule, and rules UI structure.
