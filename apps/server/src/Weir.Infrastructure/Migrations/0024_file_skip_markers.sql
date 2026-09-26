-- Weir schema 0024 (revision 0059_file_skip_markers): "Keep, but don't process again" (#785) records a marker for
-- one file in a library's watched folder, keyed to the exact bytes Weir saw when it was kept (size and modification
-- time, the same fields SourceFiles.Fingerprint reads). A watched-folder scan skips the file while its fingerprint
-- still matches; a replacement of the same name, once its size or modification time differs, is a new candidate and
-- is processed as one.
CREATE TABLE file_skip_markers (
	id INTEGER PRIMARY KEY AUTOINCREMENT,
	library_id INTEGER NOT NULL,
	relative_path TEXT NOT NULL,
	size_bytes INTEGER NOT NULL,
	mtime_ns INTEGER NOT NULL,
	created_at TEXT DEFAULT CURRENT_TIMESTAMP NOT NULL,
	CONSTRAINT fk_file_skip_markers_libraries_library_id FOREIGN KEY (library_id) REFERENCES libraries (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX uq_file_skip_markers_library_id_relative_path ON file_skip_markers (library_id, relative_path);
