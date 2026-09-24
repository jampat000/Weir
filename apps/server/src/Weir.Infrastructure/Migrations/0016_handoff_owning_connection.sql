-- Weir schema 0016 (revision 0051_handoff_owning_connection): a hand-off remembers which connection it belongs to.
--
-- Each hand-off records the connection that owns it, so the status, cancel and outcome routes accept only that
-- connection's secret. Existing rows are backfilled where exactly one enabled connection has the source key; the
-- rest stay null and keep the kind-wide check.
ALTER TABLE media_manager_handoffs ADD COLUMN connection_id INTEGER;

UPDATE media_manager_handoffs
SET connection_id = (
    SELECT c.id FROM media_manager_connections c
    WHERE c.kind = media_manager_handoffs.source_key AND c.enabled IS 1
)
WHERE (
    SELECT count(*) FROM media_manager_connections c
    WHERE c.kind = media_manager_handoffs.source_key AND c.enabled IS 1
) = 1;

CREATE INDEX ix_media_manager_handoffs_connection_id ON media_manager_handoffs (connection_id);
