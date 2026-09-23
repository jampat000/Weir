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

Where a page has a real "now", it leads with it, full width, before anything else. The lead band
(`.mm-lead-band`) is one bordered band whose segments are as wide as the number each carries. Each
segment is a control that opens the detail behind it.

A page with no "now" starts at rule 3. That is a correct outcome, not a shortcut. Do not invent a
lead.

Where the screens stand:

| Screen                 | Lead                                                                                  |
| ---------------------- | ------------------------------------------------------------------------------------- |
| Processing             | Its own lead: the live lanes in `weir-processing.css` (Arriving, Waiting, Working, Handing back, Just finished). It does not use the lead band. |
| History                | None. The file list and the chosen file, divided by a hairline.                        |
| Library                | None. The title is the library picker; a row of chips carries the counts and filters. |
| Settings               | None. Settings has no "now".                                                           |
| System › Logs › Events | `.mm-lead` with a `.mm-lead-caption`, no band.                                         |

The lead band primitive is still in `weir-content.css` for a page that has a real "now" to show.
If it is used:

- The band is the one place a page may be visually loud: a border, a filled surface, an accent
  rule along the top of each segment, a large number. Nothing else on the page gets any of that.
- The caption must stay true at every width. Say what a segment does ("Click a stage to filter
  the list"), not that segments are sized by count, because that stops being true once the band
  restacks.
- If the band draws a fixed set of states and the data has others, count them and say so in the
  caption. Do not drop them, and do not point two segments at the same filter.

### 2. One hero, then quiet

Where a page has a number that actually carries it, that number gets a wide tile and its supporting
numbers get narrow tiles beside it, in one row of unequal widths (`.mm-figure-row`).

- The hero is the number someone would quote if asked how the thing is going.
- Supporters qualify the hero. Two or three, not four.
- If every tile in the row is the same width, the rule has not been applied.

If the page has no single number worth that much room, skip the figure row and go to rule 3. An
invented hero is worse than no hero.

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

### Tokens

Three layout-only custom properties, defined in `weir-tokens.css`:

| Token               | Default | Set where                        | For                                                   |
| ------------------- | ------- | -------------------------------- | ----------------------------------------------------- |
| `--mm-flow-share`   | `1`     | Inline, per band segment         | That segment's share of the band's width (unitless)   |
| `--mm-meter-fill`   | `0%`    | Inline, per `.mm-figure__meter`  | How full the bar is                                   |
| `--mm-band-stacked` | `0`     | `weir-content.css` only          | `1` once the band has restacked. Never set it from a page. |

Everything else is an existing `--mm-*` token. `apps/web/scripts/check-design-tokens.mjs` fails the
build on any `var(--mm-…)` that `weir-tokens.css` does not define.

### Rule 1: the lead

| Class                           | Element               | For                                                   |
| ------------------------------- | --------------------- | ----------------------------------------------------- |
| `.mm-lead`                      | `div`                 | Wraps the whole lead: interrupts, band, caption, figure row |
| `.mm-lead-band`                 | `div`                 | The band: one proportional row, or a stacked list     |
| `.mm-lead-band__segment`        | `button`              | One segment. Set `--mm-flow-share` inline             |
| `.mm-lead-band__segment--empty` | modifier              | Count is zero: grey rule, dimmed value                |
| `.mm-lead-band__segment--live`  | modifier              | Something is happening here now                       |
| `.mm-lead-band__label`          | `span`                | The stage name                                        |
| `.mm-lead-band__value`          | `span`                | The count                                             |
| `.mm-lead-band__hint`           | `span`                | One short line saying what the stage means            |
| `.mm-lead-band__go`             | `span`, `aria-hidden` | "Filter →", shown on hover and keyboard focus         |
| `.mm-lead-band__pulse`          | `i`, `aria-hidden`    | The live dot. Honours `prefers-reduced-motion`        |
| `.mm-lead-caption`              | `p`                   | One line under the band or lead                       |

A segment must be a real `<button>` or `<a>`, never a `div` with `onClick`.

Weighting: share is the raw count, floored at 6% of the total so an empty stage still reads as a
stage: `share = total === 0 ? 1 : Math.max(count, total * 0.06)`.

The band never wraps. It is either one row, where every segment gets at least `6rem` and a bigger
count always draws wider, or a stacked list with one segment per line where the number is read
directly. The switch is a container query on `.mm-lead`, so it follows the panel's width, not the
window's. A page sets `--mm-flow-share` and nothing else. In particular, never give a segment a
`min-width`: the floor and the stacking threshold are one number, and overriding the floor makes the
band wrap, at which point widths only compare within a line.

### Rule 2: the figure row

| Class                    | Element              | For                                                          |
| ------------------------ | -------------------- | ------------------------------------------------------------ |
| `.mm-figure-row`         | `div`                | The grid. First child is the hero at 1.8fr; every later child is 1fr |
| `.mm-figure`             | `section`            | One tile                                                     |
| `.mm-figure--hero`       | modifier             | Big type on the hero's value. First child only               |
| `.mm-figure--warn`       | modifier             | The value is a bad number                                    |
| `.mm-figure__eyebrow`    | `div`                | Label on the left, optional context on the right             |
| `.mm-figure__value`      | `div`                | The number                                                   |
| `.mm-figure__unit`       | `span`               | Words on the hero number's baseline                          |
| `.mm-figure__note`       | `p`                  | One sentence under the value                                 |
| `.mm-figure__meter`      | `div`, `aria-hidden` | Percentage bar. Set `--mm-meter-fill` inline                 |
| `.mm-figure__foot`       | `div`                | Two or three smaller figures along the bottom of the hero    |
| `.mm-figure__foot-value` | `span`               | One of those numbers                                         |
| `.mm-figure__foot-label` | `span`               | Its label                                                    |

At 1280px and below the hero spans the full width with its supporters beneath; at 720px and below
everything stacks. Do not add breakpoints.

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
| `.mm-quiet-table--sortable`              | modifier              | The column headings are sort buttons; keeps the header row when the table stacks |
| `.mm-quiet-badge` / `--off`              | `span`                | A small pill beside a name                            |
| `.mm-quiet-state` / `--ok` / `--missing` | `span`                | Set or not set, as a word with a tick or warning colour |
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
3. Do not put a card anywhere below the lead. The figure tiles and dialogs are the only bordered
   surfaces outside the band.
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
11. Do not give a band segment a `min-width`.
12. Do not use a Tailwind utility to change something a `weir-*` class already sets.

---

## Gates

In `apps/web`: `npm ci`, `npm run lint`, `npm run format`, `npm run test`, `npm run build` (which
runs `check:tokens` and the bundle budget). From the repo root: `node scripts/check-dead-code.mjs`
and `node scripts/check-agent-docs.mjs`.

Then look at the page in dark and light, at 1440 and 1024 wide, with data and on a fresh empty
install. Unit tests have let a blank page ship before.

A page with a band also needs checking narrow: 390, and either side of the width where it restacks.
Measure rather than eyeball it: at every width, each segment is at least as wide as every segment
with a smaller count, and the band is either one line or one line per segment.
