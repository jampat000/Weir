-- Weir schema 0019 (revision 0054_handoff_targets): a hand-off knows every file it covers.
--
-- A folder hand-off, such as a season pack, covers several files. The manager is told once, when the last of them has
-- finished, about all of them; telling it after each file made it import the first and treat the hand-off as done.
--
-- 1. One row for each file a hand-off covers, from intake on, with what that file's pass came to.
CREATE TABLE media_manager_handoff_targets (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    handoff_row_id INTEGER NOT NULL,
    -- The file's path in the watched folder, the same key as files.relative_path.
    relative_path TEXT NOT NULL,
    -- 'completed', 'passed-through', 'failed' or 'cancelled' once the file has a final result; null until then.
    result TEXT,
    -- The copy Weir wrote for the file, as Weir sees it, when it has one.
    output_file TEXT,
    -- What happened to this one file, in the words the report uses.
    message TEXT,
    CONSTRAINT fk_media_manager_handoff_targets_handoff_row_id FOREIGN KEY (handoff_row_id) REFERENCES media_manager_handoffs (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX uq_media_manager_handoff_targets_handoff_row_id_relative_path ON media_manager_handoff_targets (handoff_row_id, relative_path);

-- 2. What Weir last reported to the manager ('completed' or 'failed'), so two passes finishing together report once,
--    and the output files that report named, in the manager's own paths.
ALTER TABLE media_manager_handoffs ADD COLUMN reported_status TEXT;

ALTER TABLE media_manager_handoffs ADD COLUMN output_files_json TEXT;
