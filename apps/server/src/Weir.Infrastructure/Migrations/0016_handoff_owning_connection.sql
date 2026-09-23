-- Weir schema 0016 (revision 0051_handoff_owning_connection): a hand-off remembers which connection it belongs to.
--
-- A webhook secret used to authenticate against any enabled connection of the same kind, so a second connection of
-- that kind (a 4K Radarr beside a 1080p one) could drive or read another connection's hand-offs with its own secret.
-- Closing that means every hand-off route must know which connection owns it, not merely its kind.
--
-- Existing rows are backfilled only where it is unambiguous: a source_key with exactly one enabled connection. A
-- source_key with several connections, or none (including the connection-less "native" source, which never has a
-- row here), is left null; a null owner keeps today's kind-wide check rather than gaining a new restriction.
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
