-- Weir schema 0037 (revision 0072_server_starts): one row each time the server starts, so System can say how often Weir restarted.
CREATE TABLE server_starts (
	id INTEGER NOT NULL,
	started_at DATETIME NOT NULL,
	CONSTRAINT pk_server_starts PRIMARY KEY (id)
);
CREATE INDEX ix_server_starts_started_at ON server_starts (started_at);
