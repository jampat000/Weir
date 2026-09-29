-- Weir schema 0027 (revision 0062_library_intake_follows_performance): a library's minimum size and wait follow Settings ›
-- Performance unless the library sets its own (#815).
--
-- Every library carried its own copy of both values, seeded at 50 MB and 60 s, and those copies won over Performance, so
-- changing Performance changed nothing for an existing library. NULL means "uses the Performance setting". SQLite cannot drop
-- NOT NULL from a column, so each column is rebuilt in place under the same name; a backup written before this migration
-- still restores, its values becoming the library's own.
--
-- A library keeps a value it set itself. It moves to "uses the Performance setting" when its value is the one it was seeded
-- with: 60 s for the wait, and for the size either 50 MB or 0, because a pass already skipped anything under Performance's
-- size whatever the library held, so 0 never meant "no minimum".
ALTER TABLE libraries ADD COLUMN min_file_size_mb_next INTEGER;
UPDATE libraries SET min_file_size_mb_next = CASE WHEN min_file_size_mb IN (0, 50) THEN NULL ELSE min_file_size_mb END;
ALTER TABLE libraries DROP COLUMN min_file_size_mb;
ALTER TABLE libraries RENAME COLUMN min_file_size_mb_next TO min_file_size_mb;

ALTER TABLE libraries ADD COLUMN min_file_age_seconds_next INTEGER;
UPDATE libraries SET min_file_age_seconds_next = CASE WHEN min_file_age_seconds = 60 THEN NULL ELSE min_file_age_seconds END;
ALTER TABLE libraries DROP COLUMN min_file_age_seconds;
ALTER TABLE libraries RENAME COLUMN min_file_age_seconds_next TO min_file_age_seconds;
