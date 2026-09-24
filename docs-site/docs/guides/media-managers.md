---
sidebar_position: 5
title: Connecting Deluno, Sonarr and Radarr
---

# Connecting Deluno, Sonarr and Radarr

Under **Settings › Media managers**, Weir can connect to the tools that manage your downloads and
library, so cleaning happens automatically instead of you moving files around by hand.

There are four kinds of connection: **Deluno**, **Sonarr**, **Radarr**, and **Something else** for
anything that can send Weir a message directly.

## How the folders fit together

However your setup is arranged, three things own three different folders, and every install works
as long as each one sticks to its own:

- **The download client** (SABnzbd, NZBGet, qBittorrent, Deluge, Transmission…) owns the
  incomplete folder and the completed folder for each category.
- **Weir** owns the watched folder (where it picks files up), the work folder (its private area
  while cleaning), and the output folder (where the cleaned copy goes). The work folder should sit
  on the same drive as the output folder, so a finished file can be moved into place instead of
  copied — it's allowed to be on a different drive, but Weir will tell you when it is.
- **The media manager** (Sonarr, Radarr, Deluno) owns its library roots and the category per
  library.

A download client's completed folder for a category should be the same folder Weir watches for
that library. Weir's output folder is the one the manager reads cleaned files back from — either
directly (Sonarr/Radarr's remote path mapping) or through Deluno's hand-off.

This is what **Settings › Libraries**' compact **Folder chain** section checks for every library,
and what **Settings › Media managers** checks per connection: whether the watched folder exists and
is readable, whether the work folder is set and on the same drive as the output folder, whether the
output folder exists and is writable, and — with a manager connected — whether its download-client
or category folders map onto the folder Weir watches. A library with no manager connected is shown
as a complete, valid Weir-only setup; there's nothing to warn about.

Weir also publishes each library's folders as a read-only API a manager can read instead of you
retyping them by hand (advertised as the `library-folders` capability at `/api/v1/intake/`), and it
can read a connected manager's or download client's own configuration and offer its folders as a
one-click suggestion in the library editor. Weir never changes a folder on its own — a suggestion is
only ever applied when you press the button, and a folder you typed yourself always stays.

## Deluno: automatic hand-off

Deluno hands a file to Weir to work on, and waits to be told it's ready. This is the fully
automatic setup: Deluno tells Weir about a new file, Weir cleans it, and Deluno is told when the
cleaned copy is ready to import. You don't move anything by hand.

The Weir library still needs a watched folder: Weir only accepts a hand-off for a file inside one.
Point it at the folder Deluno downloads into, using the same path Deluno sees.

## Sonarr and Radarr: checking before Weir touches a file

Connecting Sonarr or Radarr lets Weir check what they're still importing before it acts on a file.
This matters because your download client and Sonarr/Radarr are often still working with a file at
the same time Weir notices it in the watched folder — Weir checking their queue first avoids
racing an import that's already in progress.

When Weir cleans a file that is already in your library, it tells the connected manager to look at
that file again.

## Sonarr and Radarr: getting cleaned files imported

Sonarr and Radarr don't hand files to Weir. Instead, your download client finishes into Weir's
**watched folder**, Weir writes the cleaned copy to its **output folder**, and a **remote path
mapping** in Sonarr/Radarr points them at the output folder. They only ever see cleaned files.

### 1. Set up the library in Weir

Open **Settings › Libraries** and edit the library:

- **Watched folder**: where your download client finishes files, e.g. `/media/downloads/complete/tv`.
- **Output folder**: where Weir puts cleaned files, e.g. `/media/weir/tv`.
- **After cleaning, remove the original download**: turn this **off** if you use torrents. The torrent
  needs its files to keep seeding, and if they disappear, Sonarr stops treating the download as
  finished and never imports it. Your download client or Sonarr removes the original later, under
  your normal seeding rules. Weir remembers what it has already cleaned, so it won't clean the same
  file twice.
- **Existing output**: leave it on anything except **Keep both**. Keep both renames the cleaned file,
  and Sonarr only looks for the original name.

### 2. Connect Sonarr (or Radarr) to Weir

Under **Settings › Media managers**, add Sonarr with its address and API key. You'll find the key in
Sonarr on its **General** settings page.

### 3. Add the remote path mapping in Sonarr

The library's **Media manager** section in Weir shows the exact values, with copy buttons. In Sonarr, go
to **Settings › Download Clients › Remote Path Mappings** and press **+**:

| Field | Value |
| --- | --- |
| Host | Exactly what's in your download client's **Host** field on that same screen, e.g. `qbittorrent` |
| Remote Path | Weir's watched folder, e.g. `/media/downloads/complete/tv/` |
| Local Path | Weir's output folder, e.g. `/media/weir/tv/` |

- The output folder must already exist, or Sonarr won't save the mapping.
- Add one mapping for each download client Host.
- Keep **Completed Download Handling** switched on. This setup relies on it.
- If Sonarr, Weir and your download client see the folders at different paths, use the path **each app
  itself** sees. Giving every container the same folders at the same paths avoids that.

If Sonarr's own download client already has a folder set, the library editor offers it as the
watched folder with a **Use this as the watched folder** button, read straight from Sonarr's
`GET /api/v3/downloadclient` — press it instead of typing the path by hand. It only appears when
Sonarr reports one, and it's never applied without you pressing it.

### 4. Check it

In Weir, press **Check again** in the library's **Media manager** section. Weir reads Sonarr's remote path
mappings, download clients and queue — it never changes Sonarr's settings — and shows ✓, or a plain
explanation of what to fix.

### What you'll see

When a download finishes, Sonarr shows **"No files found are eligible for import"** until Weir is done.
That's expected: Sonarr checks again every minute, with no time limit, and imports the cleaned file as
soon as it appears. Weir only ever publishes a finished file, so Sonarr can't import one halfway through.

If Weir can't clean a file, what Sonarr sees depends on the library's **When retries run out** setting:

- **Hand the original back unchanged** (the default): Sonarr imports the original, uncleaned.
- **Keep it until someone acts**: Sonarr waits, and Weir shows the file on hold.
- **Reject the release so a different one is found**: Weir removes the download and blocklists it in Sonarr, so it searches again.

Files below the library's minimum size are never cleaned, so Sonarr waits on them indefinitely. Keep
the minimum size below your smallest real episode.

### Optional: hand back with Downloaded Scan

Sonarr and Radarr normally notice a cleaned file by scanning the remote path mapping on their own
schedule. If you'd rather Weir tell them the moment a file is ready, edit the connection under
**Settings › Media managers** and turn on **Scan for downloaded files after cleaning**. Off by
default. When it's on, after Weir writes a cleaned file to the library's output folder it asks the
connection to run its `DownloadedMoviesScan`/`DownloadedEpisodesScan` command over it — the same
command tools like Unpackerr use — with the path translated through the remote path mapping so the
manager gets its own view of the file. A manager that doesn't answer never fails the clean; Weir
just keeps relying on Sonarr's own periodic scan instead, and records what happened in Activity.

## Weir on its own, or alongside a bare download client

Weir doesn't need a media manager at all. A library with a watched, work and output folder and no
manager connected is a complete setup — the folder chain check above only looks at those three
folders and never warns about a manager being absent.

If you'd still like a watched-folder suggestion without connecting Sonarr, Radarr or Deluno, add
your download client itself — SABnzbd, NZBGet, qBittorrent, Deluge or Transmission — under
**Settings › Media managers**' **Download client** section, with its address and whatever
credentials it needs. This connection is outbound only and read only: Weir reads the client's own
completed-download folder (and, where the client organizes downloads by category, each category's
own folder) to suggest a watched folder, and never changes anything on the client. It feeds the same
suggestion list and the same folder chain check as a full media manager, so more than one manager
and a bare download client can all be connected to one install at once, each checked independently.

## Anything else

A tool that isn't Deluno, Sonarr or Radarr can still hand files to Weir directly, by posting to its
webhook endpoint (`/api/v1/intake/webhook/native` for a connection of kind **Something else**).
See the [API reference](../api) for the full request shape.
