-- Weir schema 0034 (revision 0069_file_history_orphans): when a file's history lost its file.
--
-- A file's history (file_logs) is kept for as long as Weir still knows the file (a files row exists), then for the number of
-- days set on the History page after the file is gone or forgotten. orphaned_at is the moment the retention task first saw
-- a history row with no file behind it; null means the file is still known. A row is removed once orphaned_at is older
-- than the retention setting, and the mark is cleared if the file comes back.
ALTER TABLE file_logs ADD COLUMN orphaned_at DATETIME;

CREATE INDEX ix_file_logs_orphaned_at ON file_logs (orphaned_at);
