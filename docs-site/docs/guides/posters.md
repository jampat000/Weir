---
sidebar_position: 6
title: Posters
---

# Posters

Weir shows a poster beside each title it lists: on the Pipeline, in **Just finished**, in History, on the Library page and at the top of a file's story. When it has no poster for a title, it shows the title's initials instead.

## How it finds them

Weir looks each title up once through Deluno's metadata service, which needs no account or key. It keeps the poster on your own machine, so the next time the title appears nothing is sent anywhere, and your browser only ever talks to Weir.

- **What is sent:** only the title and year Weir read from the file's name, or the ids (TMDb, TheTVDB or IMDb) that Deluno, Radarr or Sonarr already gave it for the title. Never a file name, a folder path, your library or anything about you.
- **How often:** at most one lookup per title, spaced out. A title the service doesn't know is asked about again after a week. If the service asks Weir to wait, Weir waits.
- **How long it keeps them:** a poster stays while any file of that title is in Weir. A month after the last one is gone, Weir deletes the poster.

Weir reads the title from the name by itself, so a file called `Nosferatu.1922.1080p.mkv` is looked up as Nosferatu, 1922. When a media manager hands a file over with its own ids, those are used instead, which is more exact than reading a name.

## Original language

The same lookup tells Weir what language each title was made in, so a profile's **Keep the original language** rule works without any setup: there is no provider to choose and no key to get. If the service doesn't know a title, the profile's audio preferences choose the tracks instead.

## Nothing to set up

There is no setting for posters or original languages. Operators who never want Weir to contact the service can start it with `WEIR_ARTWORK_GATEWAY_URL=off`: Weir then shows initials everywhere, and rules fall back to the audio preferences. Posters it already holds stay on disk.

## Credits

This product uses the TMDB API but is not endorsed or certified by TMDB. TV metadata provided by [TheTVDB](https://thetvdb.com). Both credits are shown in **System › About**.
