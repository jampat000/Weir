-- Weir schema 0038 (revision 0073_library_change_reason): why a library file needs cleaning, when the scan can say
-- (new, replaced, rules_changed). The scan reads the previous index before it writes the next one, so a reason is carried
-- forward from scan to scan until the file stops needing cleaning.
ALTER TABLE library_files ADD COLUMN change_reason VARCHAR(16);
