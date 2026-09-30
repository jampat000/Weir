-- Weir schema 0028 (revision 0063_connection_nickname): an optional short nickname for a media manager or download client
-- connection, shown after the name Weir derives from where it runs ("Radarr on nas · 4K"). Null means no nickname, which is
-- what every existing connection has after this migration.
ALTER TABLE media_manager_connections ADD COLUMN nickname TEXT;
ALTER TABLE download_client_connections ADD COLUMN nickname TEXT;
