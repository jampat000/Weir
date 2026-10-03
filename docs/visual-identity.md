# Weir — visual identity (web shell)

The palette is shared with Deluno so the two apps read as siblings: a deep blue-black base, one blue
brand colour, and colour held back for signals. Dark is the default; light keeps the same roles on a
toned-down grey page. Type is Inter for the interface and JetBrains Mono for keyboard hints and code;
release and file names use the system monospace so characters can be compared.

`apps/web/src/styles/weir-tokens.css` is the source of truth. This table is a summary of it, not a second
definition — if the two disagree, the stylesheet is right.

| Token                                   | Dark      | Light     | Use                                                          |
|-----------------------------------------|-----------|-----------|--------------------------------------------------------------|
| `--mm-bg`                               | `#0c0f18` | `#e1e4e9` | The page                                                     |
| `--mm-card-bg` (`--mm-slate`)           | `#131a25` | `#eff0f4` | A panel                                                      |
| `--mm-surface-elevated`                 | `#181f2a` | `#f7f8fa` | Menus and dialogs, one step above a panel                    |
| `--mm-surface-2`                        | `#1a222d` | `#dbdee5` | A card inside a panel                                        |
| `--mm-surface-3`                        | `#212936` | `#cfd3db` | A track or well inside a card                                |
| `--mm-hairline`                         | `#252e3c` | `#c3c8d1` | Rules and quiet borders                                      |
| `--mm-sidebar-bg`                       | `#070b12` | `#d1d6de` | The side menu                                                |
| `--mm-text` (`--mm-stone`)              | `#f3f4f7` | `#171c26` | Primary text                                                 |
| `--mm-text2`                            | `#a9b0bc` | `#525966` | Muted text                                                   |
| `--mm-primary` (`--mm-accent`)          | `#52a5ff` | `#125eba` | The brand colour: links, the current page, primary actions   |
| `--mm-success` / `--mm-warning` / `--mm-destructive` / `--mm-info` | `#25e475` / `#fb8d2d` / `#ef4d4d` / `#41a7fb` | `#168846` / `#ab4705` / `#ce1212` / `#0872c9` | Fills for bars, dots and edges |
| `--mm-status-*-text`                    | tuned     | tuned     | Words in a signal colour, at 4.5:1 or better on every surface |

The Pipeline's five stations each have a fill colour for bars, dots and edges and a `-text` twin for
words set in it, nudged until it reads at 4.5:1 on a panel: `--mm-lane-incoming` (purple),
`--mm-lane-queued` (teal), `--mm-lane-analysing` (blue), `--mm-lane-processing` (green) and
`--mm-lane-delivering` (gold).

Shapes: controls 10px (`--mm-radius-control`), fields 12px, tiles 14px, panels and dialogs 16px.

Status colours (`--mm-status-*`), input wells, shadows and the type ramp are defined in the same file
and are deliberately not duplicated here. The shared panel parts (`Panel`, `StatTile`, `Chip`,
`BarListRow`, `SegmentedControl`) live in `apps/web/src/components/panels/` and are styled in
`weir-panels.css`.

**Implementation:** `weir-tokens.css`, `weir-shell.css`, `weir-header.css`, `weir-sidebar.css`,
`weir-sidebar-nav.css`, `weir-panels.css`, `weir-content.css`, and the screen stylesheets
`weir-processing.css`, `weir-history.css` and `weir-library.css`.
