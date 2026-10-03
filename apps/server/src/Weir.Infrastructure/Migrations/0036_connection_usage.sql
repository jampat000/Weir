-- Weir schema 0036 (revision 0071_connection_usage): how long the last call to a media manager or download client took, and when
-- Weir last talked to it (or it last called Weir). Both are null until Weir has seen a call after this migration.
ALTER TABLE media_manager_connections ADD COLUMN last_answer_ms INTEGER;
ALTER TABLE media_manager_connections ADD COLUMN last_used_at DATETIME;
ALTER TABLE download_client_connections ADD COLUMN last_answer_ms INTEGER;
ALTER TABLE download_client_connections ADD COLUMN last_used_at DATETIME;
