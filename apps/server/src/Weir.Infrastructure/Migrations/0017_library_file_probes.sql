-- Weir schema 0017 (revision 0052_library_file_probes).
--
-- 1. A file's ffprobe document moves out of library_files into its own table (#715). SQLite stores a row's columns in
--    order, and probe_json (several kilobytes, mostly in overflow pages) sat ahead of the columns #568 added. Every
--    Library total and Problems count had to step over it to reach link_count, problem_kind or audio_summary. Only a
--    scan's probe cache and a hand-picked clean read the document, so it is kept apart and read only there.
CREATE TABLE library_file_probes (
	library_file_id INTEGER NOT NULL,
	probe_json TEXT NOT NULL,
	CONSTRAINT pk_library_file_probes PRIMARY KEY (library_file_id),
	CONSTRAINT fk_library_file_probes_library_files_library_file_id FOREIGN KEY(library_file_id) REFERENCES library_files (id) ON DELETE CASCADE
);

INSERT INTO library_file_probes (library_file_id, probe_json)
SELECT id, probe_json FROM library_files WHERE probe_json IS NOT NULL;

ALTER TABLE library_files DROP COLUMN probe_json;
