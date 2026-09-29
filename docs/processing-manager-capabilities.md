# Processing and media-manager coverage

Processing's basic watched-folder remux is standalone. After a file has passed the
local age, settling, access, schedule, and lifecycle checks, it can be processed
without Radarr, Sonarr, Deluno, or another manager.

Weir's Settings call each route a file takes (watched folder, work folder, output folder and
its rules) a **workflow**. There are two kinds, and both can run side by side:

- **Weir only (local folders)**: no manager or download client is involved.
- **Linked to a media manager** (Deluno, Sonarr or Radarr): the workflow is described in the source's own
  words. For Deluno that is its download client **category** and the Deluno **library** the file is
  imported into (its **Library folder**). For Radarr and Sonarr it is the download client
  **category** and the **root folder**.

The API and this document's field names still say "library" for a workflow (for example
`/api/v1/processing/libraries`), so existing integrations keep working. Weir's **Library** menu,
which cleans files already in place, keeps its name.

| Capability | Standalone | Radarr/Sonarr | Deluno | Native hand-off |
| --- | --- | --- | --- | --- |
| Watched-folder remux | Supported; local safety gates apply | Supported | Supported | Supported |
| Upstream import protection | Reduced safety: no upstream import check | Enhanced when the connection answers | Enhanced when the connection answers | Depends on the hand-off signal |
| Workflow discovery/sync | Manual workflows | Optional convenience | Optional convenience | Optional convenience |
| Destructive cleanup requiring manager truth | Safely held until Weir can confirm | Available when the manager answers | Available when the manager answers | Depends on the hand-off contract |
| Callback/hand-off | Not required | Integration-specific | Integration-specific | Supported where configured |

Each workflow reports one of three coverage states (`manager_coverage` on
`GET /api/v1/processing/libraries`):

- `connected`: the linked connection passed its latest test.
- `no_upstream_signal`: no manager is linked, or a linked manager has not yet
  returned a successful signal. This does not mean that its queue is empty.
- `unreachable`: a linked manager failed its latest connection test. Local
  remux remains possible, but manager-truth-dependent cleanup is held.

Settings › Workflows shows which manager each workflow is linked to, and a
warning when that manager did not answer its last check.

Connect or repair a manager from Settings › Media managers. A manually created
workflow remains valid and is never deleted or disabled merely because it has no
manager link.

## How each manager picks up Weir's output

- **Deluno** hands each finished download to Weir over its API and imports the
  cleaned file itself. A workflow only needs Deluno's folders: the one its
  downloads arrive in as the watched folder (a hand-off must sit inside it), and
  its processed-output folder as the output folder. The workflow editor reads
  both from Deluno's manifest and offers to fill them in. Where Deluno reaches a
  folder by another path (a NAS mount), a path mapping in Deluno (Settings ›
  Media Management › Processing Workflow › Weir › Path mappings) translates it.
  Deluno does not publish its mappings, so Weir shows such a folder as not
  verified rather than as a fault; its check follows a junction or symbolic link,
  so the same folder reached two ways is not reported as a difference. A hand-off
  is still accepted only when its path, as Deluno sends it, is inside the watched
  folder.
- **Sonarr and Radarr** are set up by hand, the way FileFlows documents it: the
  download client finishes into Weir's watched folder, and a remote path mapping
  in Sonarr/Radarr (Settings › Download Clients › Remote Path Mappings) maps that
  folder to Weir's output folder, so Completed Download Handling only ever looks
  at cleaned files. Weir writes each output under the same relative path, name
  and extension as the download, and publishes it in one step. With a torrent
  client, turn off the workflow's "After cleaning, remove the original download":
  Sonarr/Radarr only import a download the client reports as completed, and a
  torrent whose files Weir removed reports missing files instead. Kept originals
  are recognised by size and modification time and never cleaned twice. The workflow editor
  shows the exact Host, Remote Path and Local Path, and checks them against the
  saved connection with `GET` requests only; Weir never changes a manager's
  settings.
