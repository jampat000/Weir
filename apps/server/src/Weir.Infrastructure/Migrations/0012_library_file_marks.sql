-- Weir schema 0012 (revision 0047_library_file_marks): what a person decided about one library file, and what
-- Weir did to it, kept apart from the scan index.
--
-- `library_files` is the scan's own picture: every scan deletes it and writes it again, so nothing stored there
-- outlives a rescan. Two things must: that Weir cleaned this file (otherwise a cleaned file quietly becomes one
-- more file that "matches your rules", and the only record is Activity), and that a person told Weir to leave
-- this file alone (which has to mean "for ever", not "until the next scan").
CREATE TABLE library_file_marks (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    library_id INTEGER NOT NULL,
    path TEXT NOT NULL,
    -- When Weir last finished cleaning this file. Null when it never has.
    cleaned_at TEXT,
    -- A person asked Weir to leave this file alone. A scan still lists it; nothing cleans it.
    leave_alone BOOLEAN DEFAULT '0' NOT NULL,
    leave_alone_at TEXT,
    created_at TEXT DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at TEXT DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT fk_library_file_marks_libraries_library_id FOREIGN KEY (library_id) REFERENCES libraries (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX uq_library_file_marks_library_id_path ON library_file_marks (library_id, path);

CREATE INDEX ix_library_file_marks_library_id_leave_alone ON library_file_marks (library_id, leave_alone);
