-- Weir schema 0013 (revision 0048_cleanup_intervals): how often each Cleanup job runs, set in Settings › Cleanup.
--
-- Until now the interval of the leftover-work-file sweep and the failed-download cleanup came only from environment
-- variables, and whether each ran was read once when Weir started, so switching one on in the app did nothing until a
-- restart (James, 23 Sep 2026: "Cleanup, with its own schedule"). NULL keeps the interval the environment gives, which
-- is what every install runs with today.
ALTER TABLE operator_settings ADD COLUMN work_temp_stale_sweep_interval_seconds INTEGER;

ALTER TABLE operator_settings ADD COLUMN failure_cleanup_interval_seconds INTEGER;
