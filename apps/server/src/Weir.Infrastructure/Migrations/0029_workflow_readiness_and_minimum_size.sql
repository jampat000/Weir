-- Weir schema 0029 (revision 0064_workflow_readiness_and_minimum_size): a new file's wait and the smallest file a workflow
-- takes live on the workflow alone.
--
-- A workflow had three waits: the wait after a file last changed (its own or Settings > Performance's), a hold on every new
-- file, and a wait for the size to stop growing. They become one: a file is ready once neither its size nor its last-changed
-- time has moved for ready_after_seconds. Each workflow gets the longest wait it had, so nothing is picked up sooner:
-- the age (Performance's where the workflow had none) plus the hold, or the size wait when that is longer. A workflow that
-- ignored size changes never waited for the size, so that wait counts as 0. The result is capped at two weeks, as much as
-- the old waits could add up to.
--
-- The minimum size becomes a value every workflow holds: its own where it had one, Performance's where it followed that.
-- Performance keeps neither setting, so those two columns go. SQLite cannot add NOT NULL to a column in place, so the
-- minimum size is rebuilt under the same name. A backup from before this migration still restores: see
-- ConfigurationBundleIntakeUpgrade.
ALTER TABLE libraries ADD COLUMN ready_after_seconds INTEGER NOT NULL DEFAULT 60;
UPDATE libraries SET ready_after_seconds = MIN(
    1209600,
    MAX(
        0,
        COALESCE(min_file_age_seconds, (SELECT min_file_age_seconds FROM operator_settings WHERE id = 1), 60) + (hold_minutes * 60),
        CASE WHEN ignore_size_changes = 1 THEN 0 ELSE file_detection_interval_seconds END));

ALTER TABLE libraries ADD COLUMN min_file_size_mb_next INTEGER NOT NULL DEFAULT 50;
UPDATE libraries SET min_file_size_mb_next = MAX(
    0,
    COALESCE(min_file_size_mb, (SELECT min_input_file_size_mb FROM operator_settings WHERE id = 1), 50));
ALTER TABLE libraries DROP COLUMN min_file_size_mb;
ALTER TABLE libraries RENAME COLUMN min_file_size_mb_next TO min_file_size_mb;

ALTER TABLE libraries DROP COLUMN min_file_age_seconds;
ALTER TABLE libraries DROP COLUMN hold_minutes;
ALTER TABLE libraries DROP COLUMN file_detection_interval_seconds;

ALTER TABLE operator_settings DROP COLUMN min_file_age_seconds;
ALTER TABLE operator_settings DROP COLUMN min_input_file_size_mb;
