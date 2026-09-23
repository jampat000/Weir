-- Weir schema 0014 (revision 0049_link_imported_libraries): every library imported from a media manager is linked to it
-- (#651).
--
-- Importing a library recorded which connection it came from (discovered_from_connection_id) but never wrote the
-- link in library_manager_links, and no screen could add one, so no library was linked to any manager: Weir could not
-- reject a bad download through Sonarr or Radarr, and every library read "No upstream signal". Imports write the link
-- from now on; this writes it for the libraries already imported. A link that is already there is left as it is.
INSERT OR IGNORE INTO library_manager_links (library_id, connection_id)
SELECT libraries.id, libraries.discovered_from_connection_id
FROM libraries
JOIN media_manager_connections ON media_manager_connections.id = libraries.discovered_from_connection_id
WHERE libraries.discovered_from_connection_id IS NOT NULL;
