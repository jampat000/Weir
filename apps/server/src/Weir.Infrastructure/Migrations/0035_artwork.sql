-- Weir schema 0035 (revision 0070_artwork): posters for the files Weir shows, looked up once per title.
--
-- artwork_enabled is the Rules page's Artwork switch: on by default, and off stops new lookups and hides every poster.
-- artwork_lookups holds one row per title (keyed by TMDb id when a media manager supplied one, otherwise by name and year)
-- and what became of asking about it; artwork_posters holds one row per stored image (the image itself lives under
-- WEIR_HOME/artwork/posters); artwork_files says which title each file belongs to.
ALTER TABLE suite_settings ADD COLUMN artwork_enabled BOOLEAN NOT NULL DEFAULT 1;

CREATE TABLE artwork_posters (
	poster_id TEXT NOT NULL,
	source_ref TEXT NOT NULL,
	content_type TEXT NOT NULL,
	size_bytes INTEGER NOT NULL,
	created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
	CONSTRAINT pk_artwork_posters PRIMARY KEY (poster_id)
);

-- outcome is pending (not asked yet, or to be asked again at retry_at), found (poster_id names the image) or
-- missing (the service does not know the title; asked again at retry_at). poster_ref is a reference to the image (a TMDb file name or an https address) that a
-- media manager supplied, which saves the search.
CREATE TABLE artwork_lookups (
	lookup_key TEXT NOT NULL,
	media_scope TEXT NOT NULL,
	title TEXT NOT NULL,
	year INTEGER,
	tmdb_id INTEGER,
	tvdb_id INTEGER,
	imdb_id TEXT,
	poster_ref TEXT,
	priority INTEGER NOT NULL DEFAULT 0,
	outcome TEXT NOT NULL DEFAULT 'pending',
	poster_id TEXT,
	attempts INTEGER NOT NULL DEFAULT 0,
	retry_at DATETIME,
	created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
	CONSTRAINT pk_artwork_lookups PRIMARY KEY (lookup_key),
	CONSTRAINT fk_artwork_lookups_artwork_posters_poster_id FOREIGN KEY (poster_id) REFERENCES artwork_posters (poster_id)
);

CREATE INDEX ix_artwork_lookups_outcome_retry_at ON artwork_lookups (outcome, retry_at);

-- lookup_key is null for a file whose name gave no title, so it is not read again on every pass.
CREATE TABLE artwork_files (
	library_id INTEGER NOT NULL,
	relative_path TEXT NOT NULL,
	lookup_key TEXT,
	season INTEGER,
	episode INTEGER,
	CONSTRAINT pk_artwork_files PRIMARY KEY (library_id, relative_path),
	CONSTRAINT fk_artwork_files_artwork_lookups_lookup_key FOREIGN KEY (lookup_key) REFERENCES artwork_lookups (lookup_key) ON DELETE CASCADE
);

CREATE INDEX ix_artwork_files_lookup_key ON artwork_files (lookup_key);
