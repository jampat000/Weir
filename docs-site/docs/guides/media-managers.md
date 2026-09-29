---
sidebar_position: 5
title: Connecting Deluno, Sonarr and Radarr
---

# Connecting Deluno, Sonarr and Radarr

Under **Settings › Media managers**, Weir can connect to the tools that manage your downloads and
library, so cleaning happens automatically instead of you moving files around by hand.

There are four kinds of connection: **Deluno**, **Sonarr**, **Radarr**, and **Something else** for
anything that can send Weir a message directly.

## Two kinds of workflow

A **workflow** is one route a file takes through Weir: a watched folder, a work folder, an output
folder, and the rules and schedule that apply. You manage them under **Settings › Workflows**.
There are two kinds, and both can run side by side in one Weir:

- **Weir only (local folders).** Weir watches a folder you choose and writes the cleaned file to
  another. No manager or download client is involved.
- **Linked to a media manager or download client.** The workflow's watched folder is where the
  download client finishes files, and its output folder is where the manager imports from. Weir
  describes the link in the source's own words:
  - **Deluno:** the download client **category** the file comes from, and the Deluno **library**
    (with its **Library folder**) it is imported into.
  - **Radarr and Sonarr:** the download client **category**, and the **root folder** the file is
    imported into.

For example, a linked workflow reads like this: comes from Deluno's download client, category
**movies** → Weir works in its work folder → cleaned into the output folder → Deluno imports it
into its library **Movies**. A local one reads: watches `D:\Kids\Incoming` → works in its work
folder → cleaned into `D:\Kids\Ready`.

Weir's **Library** menu is a separate thing. It cleans files that are already in place in your
media library, and it keeps its name. A workflow is the path new files take.

The API still says "library" for what the app calls a workflow (for example the `library-folders`
capability below), so existing integrations keep working.

You don't name a connection. Weir names it after the kind and the host in its address: "Deluno on
RIG", "Radarr on nas", "qBittorrent on 10.1.1.51". Two of one kind on the same host also show their
port, like "Radarr on nas (7879)". Change the address and the name follows.

## How the folders fit together

However your setup is arranged, three things own three different folders, and every install works
as long as each one sticks to its own:

- **The download client** (SABnzbd, NZBGet, qBittorrent, Deluge, Transmission…) owns the
  incomplete folder and the completed folder for each category.
- **Weir** owns the watched folder (where it picks files up), the work folder (its private area
  while cleaning), and the output folder (where the cleaned copy goes). The work folder should sit
  on the same drive as the output folder, so a finished file can be moved into place instead of
  copied — it's allowed to be on a different drive, but Weir will tell you when it is.
- **The media manager** (Sonarr, Radarr, Deluno) owns where media ends up (Deluno's library folders,
  Radarr's and Sonarr's root folders) and the download client category each one uses.

A download client's completed folder for a category should be the same folder Weir watches for
that workflow. Weir's output folder is the one the manager reads cleaned files back from — either
directly (Sonarr/Radarr's remote path mapping) or through Deluno's hand-off.

This is what **Settings › Workflows**' compact **Folder chain** section checks for every workflow,
and what **Settings › Media managers** checks per connection: whether the watched folder exists and
is readable, whether the work folder is set and on the same drive as the output folder, whether the
output folder exists and is writable, and — with a manager connected — whether its download-client
or category folders map onto the folder Weir watches. A workflow with no manager connected is shown
as a complete, valid Weir-only setup; there's nothing to warn about.

Weir also publishes each workflow's folders as a read-only API a manager can read instead of you
retyping them by hand (advertised as the `library-folders` capability at `/api/v1/intake/`), and it
can read a connected manager's or download client's own configuration and offer its folders as a
one-click suggestion in the workflow editor. Weir never changes a folder on its own — a suggestion is
only ever applied when you press the button, and a folder you typed yourself always stays.

The same `/api/v1/intake/capabilities` answer carries `machine_name`, the name of the machine Weir
runs on, so a manager can call its connection to Weir "Weir on RIG".

## Deluno: automatic hand-off

Deluno hands a file to Weir to work on, and waits to be told it's ready. This is the fully
automatic setup: Deluno tells Weir about a new file, Weir cleans it, and Deluno is told when the
cleaned copy is ready to import. You don't move anything by hand.

The Weir workflow still needs a watched folder: Weir only accepts a hand-off for a file inside one.
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

### 1. Set up the workflow in Weir

Open **Settings › Workflows** and edit the workflow:

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

The workflow's **Media manager** section in Weir shows the exact values, with copy buttons. In Sonarr, go
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

If Sonarr's own download client already has a folder set, the workflow editor offers it as the
watched folder with a **Use this as the watched folder** button, read straight from Sonarr's
`GET /api/v3/downloadclient` — press it instead of typing the path by hand. It only appears when
Sonarr reports one, and it's never applied without you pressing it.

### 4. Check it

In Weir, press **Check again** in the workflow's **Media manager** section. Weir reads Sonarr's remote path
mappings, download clients and queue — it never changes Sonarr's settings — and shows ✓, or a plain
explanation of what to fix.

A ✓ means Weir read the fact itself. Where it can only take someone's word for it, the line says so and
shows a **?** and **Not verified** instead of a tick. Sonarr and Radarr tell Weir a download client's own
folder only when the client has one set; a client that files downloads by category has no folder Weir can
read from them, so it stays not verified until you connect that client to Weir directly. Deluno publishes
each library's downloads folder and each client's category, but not where its clients really save, so a
Deluno line reads "Deluno says its Movies library downloads to …" and stays not verified. A download client
you connect to Weir directly is read for itself: each folder it saves to is checked against the watched
folder, and a client saving outside it is a problem naming both folders.

### What you'll see

When a download finishes, Sonarr shows **"No files found are eligible for import"** until Weir is done.
That's expected: Sonarr checks again every minute, with no time limit, and imports the cleaned file as
soon as it appears. Weir only ever publishes a finished file, so Sonarr can't import one halfway through.

If Weir can't clean a file, what Sonarr sees depends on the workflow's **When retries run out** setting:

- **Hand the original back unchanged** (the default): Sonarr imports the original, uncleaned.
- **Keep it until someone acts**: Sonarr waits, and Weir shows the file on hold.
- **Reject the release so a different one is found**: Weir removes the download and blocklists it in Sonarr, so it searches again.

Files below the workflow's minimum size are never cleaned, so Sonarr waits on them indefinitely. Keep
the minimum size below your smallest real episode.

### Optional: hand back with Downloaded Scan

Sonarr and Radarr normally notice a cleaned file by scanning the remote path mapping on their own
schedule. If you'd rather Weir tell them the moment a file is ready, edit the connection under
**Settings › Media managers** and turn on **Scan for downloaded files after cleaning**. Off by
default. When it's on, after Weir writes a cleaned file to the workflow's output folder it asks the
connection to run its `DownloadedMoviesScan`/`DownloadedEpisodesScan` command over it — the same
command tools like Unpackerr use — with the path translated through the remote path mapping so the
manager gets its own view of the file. A manager that doesn't answer never fails the clean; Weir
just keeps relying on Sonarr's own periodic scan instead, and records what happened in Activity.

## Weir on its own, or alongside a bare download client

A Weir-only workflow and a linked one can sit side by side. For example, one workflow can feed
Sonarr while another cleans a folder of home videos that no manager ever sees.

Weir doesn't need a media manager at all. A workflow with a watched, work and output folder and no
manager connected is a complete setup (a **Weir only** workflow) — the folder chain check above only looks at those three
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
