-- Weir schema 0043 (revision 0078_activity_current_index): the lookup behind "each file as it stands now" (ActivityFilter.CurrentOnly).
--
-- For each event it lists, the query asks whether a newer event of the same kind exists for the same file. Without this index that
-- is a walk of every event about the file's path; with it, one probe of the index.
CREATE INDEX ix_activity_events_current ON activity_events (event_type, relative_path, library_id, id);
