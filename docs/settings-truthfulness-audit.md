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
- When a file fails (a workflow's own section): "Maximum automatic attempts" is the whole number of tries, the
  first one included, so 3 means the first try and two retries; nothing else caps it. Each retry
  waits twice as long as the one before (the first delay, then double, up to an hour), and the workflow's
  folder is looked at again shortly after the wait ends, not only at the next periodic scan. A file
  Weir gives up on, because it ran out of attempts or the failure is not one the workflow retries, is
  one state: Failed, with the reason. What happens next is "When retries run out": hand the original
  back, keep it until someone acts, or reject the release. History's remove dialog and "Try again"
  work on a failed file the same way whichever it was.
- Hardware decoding (workflow editor): shown as not available, and ignored by the server while it is.
  The saved values are kept.
- Rules: audio and subtitle handling is a named rule set a workflow points at, so two workflows can
  share one. Deleting a rule set a workflow still uses is refused rather than silently stripping that
  handling.
- Performance: "Files at once" (1 to 10) decides how many files run together and needs no restart. A
  workflow follows it unless the workflow is given its own lower number. The resolution budget (runner
  capacity and per-resolution costs) only applies when "Also weigh files by resolution" is switched
  on. When files are waiting, `GET /api/v1/processing/files-at-once` and the screens that use it name
  the one limit they are waiting on. "Keep free on the output drive" is checked before a file is
  read: below it the file waits (On hold) and is looked at again, and is never recorded as done.
- Cleanup: holds the leftover-work-file sweep, "Keep a failed file's half-written copy for a day" (a failed
  copy stays in the work folder until it is a day old, at which point the sweep removes it; a restart
  removes it no sooner) and the cleaned-copies-nobody-picked-up job. There is no cleanup of the
  downloads of failed files: a failed download is handled from History's remove dialog and by the
  workflow's "When retries run out".
- History: "Keep a file's history for N days after it's gone" (0 keeps it for ever) is saved to the database
  and enforced by an hourly job. A file's history is kept for as long as Weir still knows the file, then
  for N days from the hour Weir first finds the file gone or forgotten; a file that comes back before then
  keeps it. The System › Logs retention settings are separate and unchanged.
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
  leftover work file sweep. An explicit 0 stops that timer for that media type whatever is saved in
  Settings › Cleanup; the variables cannot switch a timer on. The `..._FAILURE_CLEANUP_...` variables of
  the removed failed-download cleanup are accepted and ignored.
- `WEIR_PROCESSING_WATCHED_FOLDER_MIN_FILE_AGE_SECONDS` is retired. Setting it changes nothing: the wait
  after a file last changes is the workflow's own, or Settings › Performance's.
