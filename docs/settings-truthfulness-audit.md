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

- Libraries: each library carries its own folders, file types, exclusions, scan interval and safety
  checks, saved in the database and used by new scans and per-file work after save. Missing folders
  are warnings at runtime, not save blockers. Removing a library is refused while it still has
  queued or running work, because those jobs resolve their folders from it.
- Rules: audio and subtitle handling is a named rule set a library points at, so two libraries can
  share one. Deleting a rule set a library still uses is refused rather than silently stripping that
  handling.
- Performance: "Files at once" (1 to 10) decides how many files run together and needs no restart. A
  library follows it unless the library is given its own lower number. The resolution budget (runner
  capacity and per-resolution costs) only applies when "Also weigh files by resolution" is switched
  on. When files are waiting, `GET /api/v1/processing/files-at-once` and the screens that use it name
  the one limit they are waiting on.
- Schedule: the time zone is saved to the database and every time on that tab is read in it. Each
  library's hours are saved per library and applied without a restart. `Scan now` queues a one-off
  scan of that library.

## Startup configuration

- `GET /api/v1/processing/runtime-settings` is read-only startup configuration. Any value that needs
  an environment change and a restart must stay labelled as restart-required.
- `WEIR_PROCESSING_WORKER_COUNT` is an internal startup slot cap (default 10, the most "Files at
  once" can be), not the number of files processed at once. A server started with fewer slots than
  "Files at once" asks for says so beside the setting rather than promising a number it cannot run.
- `WEIR_PROCESSING_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_SCHEDULE_ENABLED` (default on) is a global
  switch for the periodic watched-folder scan timer. With it off, no scheduled scan runs for any
  library, whatever that library's own schedule says. A manual scan is unaffected.
