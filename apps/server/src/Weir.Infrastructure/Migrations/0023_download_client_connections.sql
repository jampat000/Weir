-- Weir schema 0023 (revision 0058_download_client_connections): #768, a direct download-client connection
-- (SABnzbd, NZBGet, qBittorrent, Deluge, Transmission), used only to suggest watched folders when a library has
-- no media manager to ask, or alongside one. Weir never changes a client's settings; this is read only.
--
-- password_ciphertext holds whichever secret the client needs (qBittorrent/Deluge's Web UI password, Transmission's
-- RPC password); api_key_ciphertext holds SABnzbd's API key. Both are encrypted the same way a manager's API key is
-- (CredentialCipher) and never returned to the web.
CREATE TABLE download_client_connections (
	id INTEGER NOT NULL,
	kind TEXT NOT NULL,
	name TEXT NOT NULL,
	enabled BOOLEAN DEFAULT '1' NOT NULL,
	base_url TEXT DEFAULT '' NOT NULL,
	username TEXT,
	password_ciphertext TEXT,
	api_key_ciphertext TEXT,
	last_connection_test_ok BOOLEAN,
	last_connection_test_at DATETIME,
	last_connection_test_detail TEXT,
	created_at DATETIME DEFAULT CURRENT_TIMESTAMP NOT NULL,
	updated_at DATETIME DEFAULT CURRENT_TIMESTAMP NOT NULL,
	CONSTRAINT pk_download_client_connections PRIMARY KEY (id),
	CONSTRAINT uq_download_client_connections_name UNIQUE (name)
);
