---
sidebar_position: 6
title: Posters
---

# Posters

Weir shows a poster beside each title it lists: on the Pipeline, in **Just finished**, in History, on the Library page and at the top of a file's story. When it has no poster for a title, or Artwork is off, it shows the title's initials instead.

## How it finds them

Weir looks each title up once through Deluno's metadata service, which needs no account or key. It keeps the poster on your own machine, so the next time the title appears nothing is sent anywhere, and your browser only ever talks to Weir.

- **What is sent:** only the title and year Weir read from the file's name, or the ids (TMDb, TheTVDB or IMDb) that Deluno, Radarr or Sonarr already gave it for the title. Never a file name, a folder path, your library or anything about you.
- **How often:** at most one lookup per title, spaced out. A title the service doesn't know is asked about again after a week. If the service asks Weir to wait, Weir waits.
- **How long it keeps them:** a poster stays while any file of that title is in Weir. A month after the last one is gone, Weir deletes the poster.

Weir reads the title from the name by itself, so a file called `Nosferatu.1922.1080p.mkv` is looked up as Nosferatu, 1922. When a media manager hands a file over with its own ids, those are used instead, which is more exact than reading a name.

## Turn it off

Under **Setup › Rules › Metadata & artwork**, switch **Artwork** off. Weir stops looking anything up and shows initials everywhere. Posters it already holds stay on disk and come back if you switch Artwork on again.

Operators who never want Weir to contact the service can also start it with `WEIR_ARTWORK_GATEWAY_URL=off`.

## Credits

This product uses the TMDB API but is not endorsed or certified by TMDB. TV metadata provided by [TheTVDB](https://thetvdb.com). Both credits are shown in **System › About**.
