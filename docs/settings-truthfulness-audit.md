# Settings truthfulness audit

Weir settings must describe runtime behaviour as shipped, not intended behaviour. Each entry below
says where a setting lives and what actually happens when it is saved.

## System

- Setup wizard (System › About): `Open setup wizard` goes straight to the guided setup at
  `/setup-wizard`. It is not a navigation item.
- Updates (System › About): shows the installed version, the latest public release known to the
  update service, and where Weir was installed from. The update mode (Auto, Download only, Notify
  only) is saved to the database.
- Backups (System › Backups): the automatic backup schedule is saved to the database, and the running
  backup worker reads the saved schedule before each tick. Export and restore act immediately.
- Security (System › Security): username, password and sessions are database-backed. The "How
  sign-in is protected" group (auth cookie, HTTPS and rate-limit configuration) is read-only and says
  it comes from startup configuration and changes only with a restart.
- Retention (System › Logs, "How long this is kept"): saved to the database and enforced by runtime
  log pruning; no restart is required.

## Settings

- Workflows (the API still calls them libraries): each workflow carries its own folders, file
  types, exclusions, scan interval and safety checks, saved in the database and used by new scans
  and per-file work after save. Missing folders are warnings at runtime, not save blockers.
  Removing a workflow is refused while it still has queued or running work, because those jobs
  resolve their folders from it. Saving one part of a workflow (its hours on Schedule, its folders in
  setup) sends back every other setting as it was, so nothing else is reset.
- Rejected files (a workflow's "When a file is rejected"): "Delete only the rejected file" applies to a file
  the workflow's own size, date or path settings turn away and to a file its rules find nothing in to
  keep. A minimum size the workflow takes from Performance never deletes a file; the file is skipped and
  left where it is.
- Hardware decoding (workflow editor): shown as not available, and ignored by the server while it is.
  The saved values are kept.
- Rules: audio and subtitle handling is a named rule set a workflow points at, so two workflows can
  share one. Deleting a rule set a workflow still uses is refused rather than silently stripping that
  handling.
- Performance: "Files at once" (1 to 10) decides how many files run together and needs no restart. A
  workflow's "Most files at once from this workflow" is a share of it: blank or 0 is no limit of its
  own, and a number above Files at once is refused when saved. A workflow already above a lowered
  Files at once still saves its other settings, and its editor says it is limited by the total. The
  resolution budget (runner capacity and per-resolution costs) only applies when "Also weigh files by
  resolution" is switched on, and then counts every job that reads a whole video: a pass however it
  arrived (a hand-off costs the resolution already recorded for its file, and is corrected to the
  measured one once its pass has probed it) and a library clean (costed by the resolution its scan
  probed). A file whose resolution is not known costs what a 1080p file does. When files are waiting,
  `GET /api/v1/processing/files-at-once` and the screens that use it name the one limit they are
  waiting on.
- Keeping space free (a workflow's "Keep at least this many GB free on the drive this workflow writes
  to", 5 GB unless set, 0 turns it off): checked before every write the workflow makes. A remux pass
  and an unchanged publish check the output drive (and the work folder's drive) before the file is
  read, and again before the output is published. A hand-back copy after retries run out checks the
  output drive before copying. A library clean needs the file's size plus this much free beside the
  file. Below it the file waits (On hold, "Waiting: the output drive has less than X free") and is
  looked at again 10, then 30, then every 60 minutes, and is never recorded as done or failed. An
  upgrade gives every workflow the value Settings › Performance had; Performance still accepts the
  old field and ignores it.
  "Keep the half-written copy" keeps a failed copy in the work folder until it is a day old, at which
  point the Cleanup sweep removes it; a restart removes it no sooner.
- Schedule: the time zone is saved to the database and every time on that tab is read in it. Each
  workflow's hours are saved per workflow and applied without a restart. `Scan now` queues a one-off
  scan of that workflow.

## Startup configuration

- `GET /api/v1/processing/runtime-settings` is read-only startup configuration. Any value that needs
  an environment change and a restart must stay labelled as restart-required.
- `WEIR_PROCESSING_WORKER_COUNT` is an internal startup slot cap (default 10, the most "Files at
  once" can be), not the number of files processed at once. A server started with fewer slots than
  "Files at once" asks for says so beside the setting rather than promising a number it cannot run.
- `WEIR_PROCESSING_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_SCHEDULE_ENABLED` (default on) is a global
  switch for the periodic watched-folder scan timer. With it off, no scheduled scan runs for any
  workflow, whatever that workflow's own schedule says. A manual scan is unaffected.
- The Cleanup timers' environment switches each govern their own media type:
  `WEIR_PROCESSING_WORK_TEMP_STALE_SWEEP_MOVIE_SCHEDULE_ENABLED` and `..._TV_SCHEDULE_ENABLED` for the
  leftover work file sweep, `WEIR_PROCESSING_MOVIE_FAILURE_CLEANUP_SCHEDULE_ENABLED` and
  `WEIR_PROCESSING_TV_FAILURE_CLEANUP_SCHEDULE_ENABLED` for the failed-download cleanup. An explicit 0
  stops that timer for that media type whatever is saved in Settings › Cleanup; the variables cannot
  switch a timer on.
- `WEIR_PROCESSING_WATCHED_FOLDER_MIN_FILE_AGE_SECONDS` is retired. Setting it changes nothing: the wait
  after a file last changes is the workflow's own, or Settings › Performance's.
