# Plan: Live, Library and Settings

## Goal

Weir opens on **Live**: every file it is working on, moving in real time, from the moment it lands to
the moment the media manager has it back. **Library** is its own place: every file already imported,
what Weir would do to it, and the actions for each one. **Settings** holds everything set up once.
The side menu is Live, Library, Settings, and nothing ever scrolls sideways at any screen size.

Signed off by James on 2026-09-22 against the design canvas
<https://claude.ai/artifact/BoPav3C8yhM4f35nMNuEaN> (version 7): Live at 1440, 1920, 2560, laptop,
tablet and phone; Library list and one file opened; Settings History and logs and Settings Libraries.

## Current State

- Four side-menu items: Home, Activity, Processing, Settings. Processing carries eight tabs (Overview,
  Libraries, Audio & subtitles, Schedules, Files, Library, Jobs, Maintenance), a leftover from when it
  was one module of a suite.
- Home and Processing › Overview are the same status band twice, under different words ("Arriving" /
  "Waiting", "Stuck" / "Failed").
- The live picture is spread over Home, Overview, Files, Jobs and Activity. The server already records
  per-file step, percent, speed and time left (`processing.file_processing_progress` activity rows,
  read by `LiveProgressStore`), but the screens only show a thin progress bar on Home.
- Library mode sits at Processing › Library, with five more tabs inside it.
- Clicking **Edit** on a library opens the editor below the fold, so nothing appears to happen.
- Home names only the first library's folders.
- Pause and the theme switch sit in their own bar above every page title.

## Scope

- `apps/web/src/layouts/app-shell.tsx`, `apps/web/src/app/router.tsx`: side menu, routes, redirects
  from the old addresses (installs exist now, so old bookmarks should land somewhere sensible).
- `apps/web/src/pages/`: a new Live page replacing Home; a top-level Library page; Settings
  consolidated into seven tabs: General, Libraries, Rules, Media managers, Processing, History and logs,
  System.
- A shared page header: title on the left, Pause and the theme switch on the right, the same height.
- `apps/server`: what Live needs that the API does not return yet (library clean jobs in the live feed,
  a throughput series, the fields a working card shows), and the keep-originals safety net.
- `apps/web/openapi/weir-openapi.json` and the generated types, for every endpoint change.
- `tests/e2e`, web unit tests, `docs/design/content-language.md`, a new ADR superseding the parts of
  ADR-0016 and the content-language rules these decisions overturn, and refreshed screenshots.

## Non-Goals

- Posters, artwork or a catalogue view. The Library is about files and tracks.
- Any change to what processing does to a file.
- The FileFlows-style expansion (transcoding, flows). Live's per-file steps must leave room for it.

## Acceptance Criteria

- Side menu: Live, Library, Settings. Live is the first screen; Home and Processing › Overview are gone.
- Live shows Arriving (with countdowns), Waiting, Working (percent, speed, time left, tracks being
  removed), Handing back and Just finished, for new downloads and library cleaning alike, moving without
  a reload. Motion honours `prefers-reduced-motion`. Every number comes from the server.
- Live adapts: five lanes wide; three columns and an icon menu on a laptop; Working first on a tablet;
  one column on a phone.
- Library: the title is the library picker (no arrow with one library, "All" first, search once there
  are many); filter chips beside search; files grouped by show and season; cleaning several at once;
  one file opens in a side panel with every track, kept or removed and why, per-file choices, clean /
  leave alone / check again, and its history.
- Settings: seven tabs across the top, no second side menu. History and logs is one list with a "Show"
  choice beside search. Each library is edited in a side panel beside the list.
- Pause and the theme switch sit in each page's title row at exactly the same height.
- Keeping the original after cleaning a library file is available per library and off by default.
- **No sideways scrolling anywhere**: an automated sweep of every page from 360 to 2560 wide, in 10 px
  steps, finds no horizontal overflow on the layout or any scroller inside it (measure `.mm-app-layout`
  and each scroller, not `document.documentElement`, which `overflow-x: clip` makes read clean).
- Proven on the local test copy with simulated downloads before anything is pushed. The gates in
  `docs/design/content-language.md` pass; CI runs the contract suite and E2E.

## Steps

1. **Done.** Settings consolidation and the new side menu, with the existing screens moved rather than
   rebuilt, so nothing configurable is lost at any point. Old addresses redirect.
2. **Done.** The Live page on today's data, and the server additions it needed (the running pass's
   status, speed, elapsed time and removed tracks; library clean jobs in the live feed; a progress row
   that keeps reporting through a long pass).
3. **Done.** The Library page: title picker, chips, grouped list, the file side panel and its actions —
   including the per-track choice the canvas shows and "leave this file alone", both of which needed a
   server that had no such idea (migration 0012, `library_file_marks`).
4. History and logs as one list; the library editor as a side panel.
5. Keep originals: setting, swap-aside on clean, restore, expiry sweep.
6. The sideways-scroll sweep, test and doc updates, the ADR, screenshots.
7. Remove display density (James, 23 Sep: the four steps are 11-13% apart and duplicate browser zoom).
   Give the Library list its own compact-rows switch instead.

## Validation Log

- 2026-09-22: local test copy running from the `C:\Projects\Weir-live` worktree with simulated media
  (a generated pool of 10 files, 34 GB, fed into the watched folders like a download client). Found on
  it: Edit opens off-screen; three files left stuck after a watched-folder move with nothing on Home to
  clear them; Home names only the first library's folders.
- 2026-09-23: Live checked in Chrome at 390, 834, 1280, 1920 and 2560 px. Nothing scrolls sideways at
  any of them; the lanes fold to three columns at 1280 and to one on a phone, and the side menu drops to
  icons by itself between 921 and 1400 px.
- 2026-09-23: the soak found four server faults, all fixed and released as 3.1.3 (#644, #645, #643,
  #646). The re-cleaning loop was the worst: 16 files cleaned about 19 times each in an afternoon.
- 2026-09-23: Library checked against a simulated library of 14 files (4 shows, 18.8 GB) built from the
  same pool. It found #648: a library scan reports nothing reclaimable for Matroska files, because their
  per-track sizes live in tags the estimate never read. Fixed on this branch.
- 2026-09-23: building the per-file track choice turned up a fifth server fault, older than any of the
  four the soak found: a library file could only ever be cleaned **once**. The clean job's dedupe key is
  one row per (library, path), and a *finished* row answered the next request, so cleaning the same file
  again queued nothing while the screen said it had. A rule change or a hand-picked plan could not reach
  a file that had been cleaned in the last `WEIR_JOB_ROWS_RETENTION_DAYS`. A terminal row is now cleared
  before the enqueue; `A_finished_clean_does_not_stop_the_same_file_being_cleaned_again` fails against
  the old code.

## Decisions

- 2026-09-22: Live replaces Home as the first screen (James).
- 2026-09-22: Settings uses tabs across the top; no second side menu (James: "I dont like 2 side menus").
- 2026-09-22: the Library title is the library picker (James picked it over tabs that adapt to the
  library count).
- 2026-09-22: History and logs is one list with a "Show" choice; Library numbers are chips beside search;
  Pause and the theme switch sit in the title row at the same height (James, option B for all three).
- 2026-09-22: keeping originals after a library clean is available, off by default (James).
- 2026-09-22: nothing scrolls sideways at any width (James's condition for signing off Live).
- 2026-09-23: display density goes; browser zoom covers size, and the Library list gets a compact-rows
  switch where density actually means something (James: the options are "too similar... an amateur way
  of doing things").
- 2026-09-23: a file whose queued pass is cancelled reads **Cancelled**, a terminal state the scan
  leaves alone (#643). Live shows nothing for it; it is history until someone queues it again.
- 2026-09-23: a library file gets the same per-track choice a held download already has (#501), and a
  per-file "leave this file alone" a rescan honours (James: "build it properly"). Both are kept in
  `library_file_marks`, beside the scan index rather than in it, because a scan rewrites `library_files`
  from scratch every time.
- 2026-09-23: the server records when it last cleaned each library file, so "Cleaned" is a real count
  and a real filter rather than a guess from Activity (James: "yes, remember it").
- 2026-09-23: a track choice is checked twice — at the request, against the tracks the scan already
  read, so a choice that cannot apply is refused rather than queued; and again in the job, against a
  fresh read plus the file's size when it was chosen, so a file that changed in between is left alone.
