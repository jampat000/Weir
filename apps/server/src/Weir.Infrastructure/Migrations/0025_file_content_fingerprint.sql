-- Weir schema 0025 (revision 0060_file_content_fingerprint): the size and modification time Weir last saw for a
-- file at the moment its title became failed or rejected (#785). History's remove dialog compares
-- this against the file on disk before "delete" or "keep" touch anything: a different release landing at the same
-- watched-folder path must never be destroyed or skipped in place of the one that actually failed. Null on a row
-- from before this migration, or where the fingerprint could not be read; either way the remove dialog refuses the
-- choice until the title fails or is rejected again.
ALTER TABLE files ADD COLUMN fingerprint_size_bytes INTEGER;
ALTER TABLE files ADD COLUMN fingerprint_mtime_ns INTEGER;
