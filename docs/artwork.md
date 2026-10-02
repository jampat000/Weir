# Posters

Weir shows a poster for the files it lists (Pipeline cards, the Just finished shelf, History, the Library page and a file's story). It gets them from Deluno's metadata service, which needs no key, and never from the browser: the web app only ever asks Weir for `/api/v1/artwork/posters/{id}`.

## Where a file's title comes from

- **A Deluno hand-off** may carry optional `title`, `year`, `tmdbId`, `tvdbId`, `imdbId`, `season`, `episode` and `posterUrl`. A malformed field is ignored, never an error. A `posterUrl` is used only when it is absolute https on `deluno-metadata-gateway.ejmdigital.workers.dev`, `image.tmdb.org`, `artworks.thetvdb.com` or `m.media-amazon.com` (`ArtworkPosterSource.ImageHosts`); anything else is ignored and the ids or the name decide.
- **Radarr and Sonarr imports** of a file Weir handed back carry the manager's own ids: Radarr's movie `tmdbId` and `imdbId`, Sonarr's series `tvdbId` and `imdbId`. They replace the title read from the name, so those files get an exact lookup. A message with no webhook secret could be from anybody, so it changes nothing; a missing or malformed id is ignored.
- **Every other file** is read from its path by `ArtworkTitleReader`: the release name, file name and folders for a film; the text before the season or episode marker for a series, so every episode shares one title.

## One lookup per title

`artwork_lookups` holds one row per title, keyed by TMDb id when known, otherwise by the normalised name and year (`ArtworkKeys`). `artwork_files` says which title each file has, and `artwork_posters` which images are stored; the images live in `WEIR_HOME/artwork/posters`, named by an opaque id derived from where the image came from.

`ArtworkResolverTask` (every 5 seconds) titles new files (`ArtworkDiscovery`) and settles up to 20 due lookups (`ArtworkResolver`), one at a time:

- A TMDb id is looked up exactly (`providerId=`); a series with only a TheTVDB id uses `/metadata/tvdb/tv/{id}`; anything else is a title search. A poster address from a hand-off skips the search.
- The service allows 120 calls a minute per address, shared with every other app on the network. Weir searches at most once every two seconds (`ArtworkRateLimiter`), pauses every lookup for as long as a 503 `Retry-After` asks (a minute when it names none, an hour at most) and for a minute after the service could not be reached.
- A title the service does not know is remembered and asked about again after seven days. A title that could not be asked about is retried after 5 minutes, doubling up to 6 hours. Files from a library scan queue behind files Weir is processing.

## Pruning

About once an hour `ArtworkResolverTask` runs `ArtworkPruner`, which needs no network and runs even with Artwork off. A file that is in neither `files` nor `library_files` is marked (`artwork_files.orphaned_at`); the mark clears if the file comes back. Thirty days after the mark (`ArtworkSchedule.GoneFileGrace`, so a re-download does not fetch its poster again) the row goes, then any title no file uses, then any poster no title uses, image and database row. What stays is one small row per title and per file Weir still knows.

## Settings and tests

- The **Artwork** switch under Setup › Rules › Metadata & artwork is `artwork_enabled` on `/api/v1/processing/metadata-provider` (on by default). Off stops new lookups and makes every `poster_url` null; stored images stay on disk.
- `WEIR_ARTWORK_GATEWAY_URL` points Weir at another gateway, or `off` for no lookups at all. The real service is a public address and must resolve to one; any other address is treated as a stand-in on this machine.
- **No test reaches the real service.** The server and contract test harnesses set the variable to `off`, and the tests that need a gateway use a fake (`FakeGateway` in `tests/contract/support`, a scripted handler in the .NET tests).
- TMDb requires its attribution text and logo wherever its data is used, and TheTVDB a link where its images show; System › About carries both.
