-- Weir schema 0015 (revision 0050_handback_outcomes): what became of each file Weir handed back (#652).
--
-- Sonarr and Radarr already tell Weir when they import a file, Deluno now does too, and Weir threw every one of those
-- messages away. So History could never say "Imported by Sonarr", and a copy a manager imported by copying or
-- hardlinking stayed in the hand-back folder for ever.
--
-- 1. The copy Weir wrote into a library's output folder, one row per file. Its size and modification time are what make
--    it safe to remove later: Weir removes a copy only while it is still exactly the file Weir wrote. A second pass over
--    the same file replaces the row, because it wrote a new copy.
CREATE TABLE handbacks (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    library_id INTEGER NOT NULL,
    -- The file's path in the watched folder, the same key as files.relative_path.
    relative_path TEXT NOT NULL,
    -- The copy, as Weir sees it, and exactly what Weir wrote.
    output_path TEXT NOT NULL,
    output_size BIGINT NOT NULL,
    output_mtime_ns BIGINT NOT NULL,
    written_at TEXT NOT NULL,
    -- What a manager said about it: 'imported', 'not-imported', or null while nobody has said.
    outcome TEXT,
    outcome_by TEXT,
    outcome_at TEXT,
    -- Where the manager put it in its library, when it said.
    imported_path TEXT,
    outcome_reason TEXT,
    -- When Weir removed its copy. Null while the copy is there, or when Weir left it.
    released_at TEXT,
    -- When Weir stopped looking after the copy (removed, already gone, or changed by someone else), and why, in words.
    settled_at TEXT,
    release_note TEXT,
    created_at TEXT DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at TEXT DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT fk_handbacks_libraries_library_id FOREIGN KEY (library_id) REFERENCES libraries (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX uq_handbacks_library_id_relative_path ON handbacks (library_id, relative_path);

CREATE INDEX ix_handbacks_settled_at ON handbacks (settled_at);

-- 2. The manager's word on a hand-off it gave Weir (POST /api/v1/intake/handoffs/{source}/{id}/outcome), its download
--    id when it sent one (so a Sonarr or Radarr import can be matched by it), and a report Weir owes a manager that was
--    not answering when the pass ended. The heartbeat sends that report once the manager answers again.
ALTER TABLE media_manager_handoffs ADD COLUMN outcome TEXT;

ALTER TABLE media_manager_handoffs ADD COLUMN outcome_at DATETIME;

ALTER TABLE media_manager_handoffs ADD COLUMN outcome_message TEXT;

ALTER TABLE media_manager_handoffs ADD COLUMN outcome_released BOOLEAN DEFAULT '0' NOT NULL;

ALTER TABLE media_manager_handoffs ADD COLUMN download_id TEXT;

ALTER TABLE media_manager_handoffs ADD COLUMN pending_report_json TEXT;

-- 3. Settings › Cleanup › Unclaimed hand-backs: off until a person switches it on (James, 23 Sep 2026), and a copy
--    nobody claimed is removed once it is this many days old. NULL interval: the built-in six hours.
ALTER TABLE operator_settings ADD COLUMN unclaimed_handback_cleanup_enabled BOOLEAN DEFAULT '0' NOT NULL;

ALTER TABLE operator_settings ADD COLUMN unclaimed_handback_window_days INTEGER DEFAULT '14' NOT NULL;

ALTER TABLE operator_settings ADD COLUMN unclaimed_handback_cleanup_interval_seconds INTEGER;
