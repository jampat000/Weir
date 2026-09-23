# UX Polish Standard

This is the app-wide UX baseline for Weir. Use it when reviewing screens before release. How the
content of a page is laid out is in [`design/content-language.md`](design/content-language.md); where
the two disagree, that document wins.

## Language

- User-facing text explains what happened and why in plain language.
- Avoid raw internal event keys, auth implementation names, library names, traceback wording, or JSON blobs unless the user explicitly opens a technical details view.
- Events describe the user outcome first, then technical detail second.
- Logs are system/application event logs for troubleshooting runtime issues, not a development changelog.
- Operator-facing messages follow [`operator-messaging-standard.md`](operator-messaging-standard.md).

## Layout

- Page bodies are borderless: a heading, a hairline, then content. Dialogs are the only card-shaped surfaces.
- Settings and System use one horizontal tab row each. It scrolls horizontally on narrow screens while keeping the tab-to-panel accessibility contract.
- Settings groups follow the order someone sets Weir up in, and are grouped by user task, not backend implementation.
- Processing is the first screen: what Weir is working on right now and anything that needs a person. A file's full story belongs in History; Weir's own events belong in System › Logs.
- The document is the page scroll owner. Do not trap signed-in pages inside a fixed-height nested scrolling pane.
- Nothing scrolls sideways at any width. Tables and lanes restack instead.
- Empty states are compact, aligned with the surrounding layout, and say what to do next.
- Long detail views are compressed by default and expandable when more information is useful.

## Visuals

- Status colors must be consistent:
  - healthy or complete: green
  - warning or review needed: amber
  - failed or blocked: red
  - informational or queued: blue/neutral
- Badges and pills use the same shape everywhere. Today there are still more than one:
  `mm-quiet-badge` / `mm-quiet-state` (Settings, System, most tables) and `mm-activity-chip` /
  `mm-status-badge` (the events list in System › Logs). New work uses the `mm-quiet-*` family.
- Font sizing stays consistent across headings, labels, body text, and compact metadata.
- Colours, type sizes and spacing come only from `apps/web/src/styles/weir-tokens.css`.

## Screen-specific baseline

- Processing shows live work, not just navigation: what is arriving, waiting, working, handing back and just finished, and anything blocking it.
- History shows every file Weir has touched. A file's processing record can show before/after file details, size saved, languages, subtitles and removals, the plan, output validation, source cleanup and duration, with the raw payload behind a further `<details>`.
- Library shows the files already in a library and what Weir would do to each. The library is picked from the title.
- System › Logs is live, filterable, searchable, color-coded, and readable at scale.
- Folder path inputs support `Browse` where technically possible and accept manual local, Docker, and UNC-style paths.

## Review rule

If a screen looks technically correct but a normal user would not understand what happened or what to do next, it is not done.
