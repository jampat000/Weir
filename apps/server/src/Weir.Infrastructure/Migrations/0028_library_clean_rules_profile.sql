-- Weir schema 0028 (revision 0063_library_clean_rules_profile): each library chooses the rules profile that cleans its
-- existing files, on the Library page.
--
-- NULL means "the same as the workflow's profile", which is what every library used until now, so an upgrade changes
-- nothing. A profile that a library points at cannot be deleted, like one a workflow points at.
ALTER TABLE libraries ADD COLUMN library_rule_set_id INTEGER REFERENCES rule_sets (id) ON DELETE RESTRICT;
