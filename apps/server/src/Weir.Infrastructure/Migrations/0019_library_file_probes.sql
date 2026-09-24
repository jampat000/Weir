-- Weir schema 0019 (revision 0054_library_file_probes).
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

-- 2. A watched-folder scan asks, for a file, whether an earlier pass already completed it (#708). It looks the completion up
--    by activity_events.relative_path, which is indexed, instead of searching every completion's detail text. The activity
--    writer fills that column from the detail's relative_media_path; a completion recorded without it gets it here, so the
--    lookup finds every completion the old search found. The value is the path stripped of surrounding whitespace and cut to
--    2000 characters, as the writer stores it.
UPDATE activity_events
SET relative_path = substr(NULLIF(trim(json_extract(detail, '$.relative_media_path'), ' ' || char(9, 10, 11, 12, 13)), ''), 1, 2000)
WHERE relative_path IS NULL
	AND event_type = 'processing.file_remux_pass_completed'
	AND json_valid(detail)
	AND json_type(detail, '$.relative_media_path') = 'text';

-- 3. The same scan, a hand-off and every automatic enqueue ask whether a pass is already pending or running for a file, by
--    the path in its payload. This index holds only those passes, so the answer no longer means reading every queued job.
--    Queries repeat its WHERE clause word for word, because SQLite uses a partial index only for a query that states it.
CREATE INDEX ix_jobs_active_remux_pass_path ON jobs (json_extract(payload_json, '$.relative_media_path'))
WHERE job_kind = 'processing.file.remux_pass.v1' AND status IN ('pending', 'leased');
