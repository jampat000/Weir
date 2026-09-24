-- Weir schema 0018 (revision 0053_query_indexes): indexes for the reads the Processing and Activity pages repeat (#714).
--
-- Each of these queries walked a whole table, or sorted one, on every refresh: tens to hundreds of milliseconds on a
-- large install, several times a minute while a page is open. Indexes only; no data changes.
--
-- One event type over a recent stretch of time: live progress on every Processing refresh, and the overview's last
-- 30 days of results.
CREATE INDEX ix_activity_events_event_type_created_at ON activity_events (event_type, created_at);
-- Activity for one library, newest first.
CREATE INDEX ix_activity_events_library_id_created_at ON activity_events (library_id, created_at);
-- System > Logs: Weir's own events, the ones not about a file (about=weir), newest first. The query names these rows
-- with exactly this expression, which is what lets SQLite use a partial index.
CREATE INDEX ix_activity_events_about_weir_created_at ON activity_events (created_at)
WHERE coalesce(relative_path, '') = '';
-- The Processing file list, most recently seen first, for every library and for one.
CREATE INDEX ix_files_last_seen_at_id ON files (last_seen_at, id);
CREATE INDEX ix_files_library_id_last_seen_at ON files (library_id, last_seen_at);
-- Jobs inspection, most recently changed first.
CREATE INDEX ix_jobs_updated_at ON jobs (updated_at);
