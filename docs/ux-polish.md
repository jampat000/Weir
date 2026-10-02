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
- Processing is the first screen: what Weir is working on right now and anything that needs a person. A file's full story belongs in Activity; Weir's own events belong in System › Logs.
- The document is the page scroll owner. Do not trap signed-in pages inside a fixed-height nested scrolling pane.
- Nothing scrolls sideways at any width. Tables and lanes restack instead.
- Every control sits on the centre of the title's text beside it: a page's controls on the page title, a card's on the
  card's title (not on the middle of the header, nor of a title with a note under it). A control that wraps to a row
  of its own is under the title, not beside it, and is not held to this.
- A page's tabs never fold into "More" because its filters need room. The filters on the title line give way instead, in
  a fixed order (`components/shell/title-line-fit.ts`): a search shrinks to a mark, pickers move into the card they
  filter, then the last chips fold, whole, into a "More" menu. The chosen chip never folds, and no chip is cut or scrolled.
- Labels and counts are never clipped. A file's name in a list row is the exception: it wraps to two lines, then an
  ellipsis, and the whole name is the row's title.
- Empty states are compact, aligned with the surrounding layout, and say what to do next.
- Long detail views are compressed by default and expandable when more information is useful.

## Visuals

- Status colours follow one scheme, set out under Status colours below.
- Red is for actions that remove, override or step outside normal running; Weir's routine work (cleaning, scans, cleanup runs) is never red.
- Badges and pills use the same shape everywhere. Today there are still more than one:
  `mm-quiet-badge` / `mm-quiet-state` (Settings, System, most tables) and `mm-activity-chip` /
  `mm-status-badge` (the events list in System › Logs). New work uses the `mm-quiet-*` family.
- Font sizing stays consistent across headings, labels, body text, and compact metadata.
- Colours, type sizes and spacing come only from `apps/web/src/styles/weir-tokens.css`.

## Status colours

Every status in Weir has a meaning, and the meaning has one colour. A feature decides which meaning a state has; no
component picks a colour for a status. The colours are `--mm-status-*` in `apps/web/src/styles/weir-tokens.css`,
the meanings are `StatusMeaning` in `apps/web/src/lib/ui/status-meaning.ts`, and `weir-status.css` draws them.

| Meaning     | Colour                                           | Examples                                                                                                                 |
| ----------- | ------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------ |
| `done`      | green                                            | Matches rules, Cleaned, Already clean, Imported by Sonarr/Radarr, Finished, Answering, In sync, backup OK, success level |
| `todo`      | blue, faint: a hollow ring, not a solid dot      | Needs cleaning, Queued, Waiting, Waiting its turn                                                                        |
| `doing`     | blue, solid; may pulse where it already animates | Cleaning, Writing 52%, Analysing, Checking, testing                                                                      |
| `attention` | amber                                            | Can't clean, On hold, Rejected, Passed through, Slow, Not verified, warnings                                             |
| `broken`    | red                                              | Weir can't read it, Couldn't finish, Failed, Not answering, errors                                                       |
| `idle`      | grey                                             | Left alone, Skipped, Off, Kept, info level                                                                               |
| payoff      | gold                                             | Space-saved figures only: "1.2 GB saved" on Today, Just finished captions, the Library Back column, Activity Saved       |

- Colour is never the only signal: a dot, ring or pill always sits beside words that say the state.
- Payoff is for the figure, as text (`mm-payoff`). It is never a status dot, and it is a different colour from amber in both themes.
- To show a status, put `data-status="<meaning>"` on the element and add `mm-status-dot` (or `<StatusDot meaning />`),
  `mm-status-text` for a coloured word, or `mm-status-pill` on a chip. A stylesheet that needs the colour for a bar or a
  stripe reads `var(--mm-st)` inside a `data-status` element.
- A new state in a model needs a row in `status-meaning-coverage.test.ts`; `status-colour-guard.test.ts` fails when a
  component uses an old status colour, a hex colour or a raw palette colour.
- The Pipeline's lanes (Incoming, Queued, Analysing, Processing, Delivering) are a flow, not a status: violet, teal,
  indigo, fuchsia and pink, never green, amber, red or the status blue.
- Buttons are separate from statuses. Red is for actions that remove, override or step outside normal running; Weir's
  routine work, cleaning included, is never red. Blue is the primary action.

## Screen-specific baseline

- Processing shows live work, not just navigation: what is arriving, waiting, working, handing back and just finished, and anything blocking it.
- Activity shows every file Weir has touched. A file's processing record can show before/after file details, size saved, languages, subtitles and removals, the plan, output validation, source cleanup and duration, with the raw payload behind a further `<details>`.
- Library shows the files already in a library and what Weir would do to each. The library is picked from the title.
- System › Logs is live, filterable, searchable, color-coded, and readable at scale.
- Folder path inputs support `Browse` where technically possible and accept manual local, Docker, and UNC-style paths.

## Review rule

If a screen looks technically correct but a normal user would not understand what happened or what to do next, it is not done.
