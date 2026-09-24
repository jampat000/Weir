# The Weir content language

How the content of a Weir page is laid out. The shell around it (sidebar, top bar, page header,
workspace tab row) is out of scope here; this document covers what sits inside a page's body.

The page-neutral primitives live in `apps/web/src/styles/weir-content.css`. Screen-specific layout
lives beside them:

| Screen             | Page component                                   | Screen stylesheet    |
| ------------------ | ------------------------------------------------ | -------------------- |
| Processing         | `apps/web/src/pages/processing/processing-page.tsx` | `weir-processing.css` |
| History            | `apps/web/src/pages/history/history-page.tsx`    | `weir-history.css`   |
| Library            | `apps/web/src/pages/library/library-page.tsx`    | `weir-library.css`   |
| Settings           | `apps/web/src/pages/settings/settings-page.tsx`  | `weir-content.css` only |
| System             | `apps/web/src/pages/system/system-page.tsx`      | `weir-content.css` only |

All of them sit under `apps/web/src/styles/` and are imported by `apps/web/src/index.css`. Colours,
type sizes and spacing come from `weir-tokens.css` only.

---

## The three rules, in priority order

### 1. The page leads with what is happening now

Where a page has a real "now", it leads with it, full width, before anything else.

A page with no "now" starts at rule 3. That is a correct outcome, not a shortcut. Do not invent a
lead.

Where the screens stand:

| Screen                 | Lead                                                                                  |
| ---------------------- | ------------------------------------------------------------------------------------- |
| Processing             | Its own lead: the live lanes in `weir-processing.css` (Arriving, Waiting, Working, Handing back, Just finished). |
| History                | None. The file list and the chosen file, divided by a hairline.                        |
| Library                | None. The title is the library picker; a row of chips carries the counts and filters. |
| Settings               | None. Settings has no "now".                                                           |
| System › Logs › Events | `.mm-lead` with a `.mm-lead-caption`.                                                   |

`.mm-lead` and `.mm-lead-caption` (in `weir-content.css`) are the only rule-1 primitives left: a
wrapper and a one-line caption under it. An earlier, bordered "lead band" primitive
(`.mm-lead-band` and its segments) existed for a page whose "now" was a handful of named counts side
by side; it had no user left once Processing's own live lanes replaced its one caller, and was
deleted along with its CSS rather than kept around unused (see "What not to do" below). If a future
page needs that shape again, design and add the primitive fresh — do not assume the deleted classes
still work.

### 2. One hero, then quiet

Where a page has a number that actually carries it, that number leads, and its supporting numbers
stay visibly smaller beside it.

- The hero is the number someone would quote if asked how the thing is going.
- Supporters qualify the hero. Two or three, not four.
- If every number reads the same weight, the rule has not been applied.

If the page has no single number worth that much room, skip this rule and go to rule 3. An invented
hero is worse than no hero. `apps/web/src/pages/system/tabs/about/update-section.tsx` and
`.../logs/server-log.tsx` both say, in a comment naming this rule, why their own facts are coequal
and neither picks one as a hero — that is what applying rule 2 and getting "no hero" looks like.

No current screen needs the room a dedicated hero primitive gives: the `.mm-figure-row`/`.mm-figure`
tile-row that rule 2 used to point at was removed with Processing's Overview tab, which was the only
page that ever had a number that size. Nothing in `weir-content.css` implements this rule today; a
page that needs it adds the primitive fresh rather than reaching for a deleted class name.

### 3. Everything below the lead is borderless

No cards, no panels, no wells. Hierarchy comes from type scale and whitespace: a section heading, a
hairline, then content.

- A section is a heading on the left, its links on the right, a hairline under both, then the
  content. `QuietSection` in `apps/web/src/components/shared/quiet-section.tsx` draws it.
- The main control in the quiet body is a text link (`.mm-quiet-link`, label ending `→`). A form's
  own Save row uses buttons, under a hairline.
- Tables lose their box. The header row is uppercase eyebrow type over a `--mm-line` hairline; body
  rows are separated by a fainter `--mm-border` hairline; the last row has none.
- Settings use one shape: `SettingsGroup` and `SettingRow` in
  `apps/web/src/components/shared/settings-group.tsx` put what the group is on the left and one
  setting per row on the right. A single field uses `Field` in
  `apps/web/src/components/shared/field.tsx` (`.mm-field`), whose width comes from what goes in it:
  `--short` for a number or time, `--medium` for a choice or short name, `--wide` for a path or
  sentence.
- Settings most people never change fold away behind `QuietDisclosure`, a `<details>` whose summary
  says what is inside ("3 of 9 on") so the fold never hides a changed value.

Dialogs are the exception: a dialog is a bordered surface over the page.

---

## Two standing rules that override the three above

### Change the shell deliberately

`apps/web/src/components/shell/**`, `apps/web/src/components/shared/workspace-shell.tsx`
(`WorkspacePage`, `WorkspacePanel`, `WorkspaceTabList`) and the parts of `weir-shell.css` that style
them are shared by every screen. Do not change them as a side effect of laying out one page's
content. If a layout needs a shell change, make that change on its own and check every screen.

### The empty state still takes over

A band or a row of zeroes on a fresh install looks broken. Whatever a page does when it has nothing
must happen ahead of the lead and the figure row. The strongest empty wins outright; a merely zero
lead becomes one sentence, not a row of zeroes (Processing's empty lanes each say "Nothing
waiting." and so on). A loading or failed load uses the same slot.

---

## A Tailwind utility cannot override a `weir-*.css` class

Read this before debugging a style that does nothing.

`apps/web/src/index.css` starts with `@import "tailwindcss"`, and Tailwind v4 emits every utility
inside `@layer utilities`. The `weir-*.css` files are imported after it and are unlayered. Cascade
layers are resolved before specificity, and an unlayered declaration outranks a declaration in any
layer. So a Tailwind utility on an element that also carries a `weir-*` class never applies to a
property that class sets, and fails silently.

- Adding specificity does not help. The layer decides first.
- Reordering the imports does not help. It is layering, not source order.
- The property belongs in the `weir-*` class. If a page needs a different value, add a modifier to
  the primitive. Tailwind's `!` suffix does win, but it hides the disagreement between the page and
  the primitive from the next reader.

Utilities are fine for anything no `weir-*` class touches, such as `min-w-0` on a plain `div` or the
layout inside a table cell.

---

## The primitives

All of these are in `apps/web/src/styles/weir-content.css`. Use them rather than page-specific layout
CSS. If a page needs something none of them do, add it as another page-neutral primitive.

`apps/web/scripts/check-design-tokens.mjs` fails the build on any `var(--mm-…)` that
`weir-tokens.css` does not define.

### Rule 1: the lead

| Class              | Element | For                                          |
| ------------------ | ------- | --------------------------------------------- |
| `.mm-lead`         | `div`   | Wraps the lead: interrupts, then the caption   |
| `.mm-lead-caption` | `p`     | One line under the lead                        |

Rule 1's other primitive, a bordered "lead band" of proportional segments sized by count, was
deleted with its CSS once Processing's live lanes replaced its only caller (see the rule-1
discussion above). There is nothing else to list here; a page that needs the band shape again
designs and adds that primitive fresh, rather than assuming the deleted `.mm-lead-band*` classes
still work.

### Rule 2: the figure row

Deleted along with its CSS (`.mm-figure-row`, `.mm-figure` and their parts) when Processing's
Overview tab, its only caller, folded into the live board (see the rule-2 discussion above).
There is nothing under this heading today; a page that needs a hero tile row again adds the
primitive back to `weir-content.css` rather than reaching for a `.mm-figure*` class name in its
own CSS.

### Rule 3: the quiet body

| Class                                    | Element               | For                                                   |
| ---------------------------------------- | --------------------- | ----------------------------------------------------- |
| `.mm-quiet-stack`                        | `div`                 | The page body. Vertical rhythm between sections       |
| `.mm-quiet-section`                      | `section`             | One borderless section                                |
| `.mm-quiet-section__head`                | `div`                 | Heading row with the hairline under it                |
| `.mm-quiet-section__title`               | `h2`                  | The heading. Give it an id and `aria-labelledby` the section |
| `.mm-quiet-section__aside`               | `div`                 | The section's links, aligned with the heading         |
| `.mm-quiet-section__body`                | `div`                 | The content under the hairline                        |
| `.mm-quiet-group`                        | `section`             | A group of fields in a long form: an eyebrow over a hairline (`QuietFieldGroup`) |
| `.mm-quiet-group__step`                  | `span`, `aria-hidden` | An optional step cue, kept out of the accessible name |
| `.mm-quiet-actions`                      | `div`                 | A form's own Save row: a hairline, then the buttons   |
| `.mm-quiet-link`                         | `button` / `a`        | The quiet body's control. Label ends with `→`         |
| `.mm-quiet-note`                         | `p`                   | A muted paragraph: empty states, captions, explanations |
| `.mm-quiet-table-wrap`                   | `div`                 | Horizontal scroll container for a table               |
| `.mm-quiet-table`                        | `table`               | The borderless table                                  |
| `.mm-quiet-badge` / `--off`              | `span`                | A small pill beside a name                            |
| `.mm-interrupt`                          | `ul`                  | Things that are broken or blocking, above everything else |
| `.mm-field`, `--short` / `--medium` / `--wide` | `label`         | One setting: label, control, hint (`Field`)           |
| `.mm-setgroup`, `.mm-setrow`             | `section`, `div`      | A settings group and its rows (`SettingsGroup`, `SettingRow`) |

Every `td` in a `.mm-quiet-table` needs `data-label="…"` matching its column heading. Below 760px the
table becomes stacked rows and that attribute is the label.

`.mm-quiet-table-wrap` is positioned so an `.sr-only` heading inside a scrolling table stays inside
the wrapper. To check for sideways overflow, measure `.mm-app-layout`, not
`document.documentElement`: `.mm-app-layout` has `overflow-x: clip`, so the document reports no
overflow either way.

A `.mm-quiet-link` that is busy or unavailable takes `disabled` (or `aria-disabled`), and its label
says so as well ("Exporting…").

An interrupt is a list of sentences with a link each, not a card, banner or coloured box.

---

## What not to do

1. Do not change the shell as a side effect of page work. See above.
2. Do not add a colour, font, shadow or radius. Only tokens already in `weir-tokens.css`. A new
   `--mm-*` token must be pure layout.
3. Do not put a card anywhere below the lead. Dialogs are the only bordered surface there.
4. Do not build an equal-width tile row. `repeat(auto-fit, minmax(…, 1fr))` for numbers is the
   pattern this replaces.
5. Do not invent a lead, a hero, a sparkline, a trend arrow or a percentage the API does not return.
6. Do not draw a band or row of zeroes. Route every empty through the empty-state rule.
7. Do not make a segment or a row a clickable `div`. Buttons and links only.
8. Do not delete a test because it fails on new markup. If it asserts layout, update it. If it
   asserts behaviour (a filter works, an empty state wins, a secret never reaches the DOM), keep it
   and make it pass.
9. Do not leave superseded CSS behind. When the last user of a rule goes, delete the rule in the
   same PR.
10. Do not change what a `data-testid` points at. The E2E suite in `tests/e2e/weir` relies on them.
11. Do not use a Tailwind utility to change something a `weir-*` class already sets.

---

## Gates

In `apps/web`: `npm ci`, `npm run lint`, `npm run format`, `npm run test`, `npm run build` (which
runs `check:tokens` and the bundle budget). From the repo root: `node scripts/check-dead-code.mjs`
and `node scripts/check-agent-docs.mjs`.

Then look at the page in dark and light, at 1440, 1024 and 390 wide, with data and on a fresh empty
install. Unit tests have let a blank page ship before.
