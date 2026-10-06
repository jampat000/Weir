-- Weir schema 0039 (revision 0074_schema_version_table): the table that records the schema revision is named
-- schema_version, with one revision column. The recorded row is kept.
ALTER TABLE alembic_version RENAME TO schema_version;
ALTER TABLE schema_version RENAME COLUMN version_num TO revision;
