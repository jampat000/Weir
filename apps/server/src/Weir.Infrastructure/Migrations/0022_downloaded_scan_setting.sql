-- Weir schema 0022 (revision 0057_downloaded_scan_setting): the optional downloaded-scan hand-back for
-- Radarr and Sonarr connections that do not use Weir's hand-off protocol (DownloadedMoviesScan/DownloadedEpisodesScan).
--
-- Off by default: Weir never calls a manager's command API until the connection opts in.
ALTER TABLE media_manager_connections ADD COLUMN downloaded_scan_enabled BOOLEAN DEFAULT '0' NOT NULL;
