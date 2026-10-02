import { describe, expect, it } from "vitest";

/*
 * A status gets its colour from its meaning (lib/ui/status-meaning.ts) and nowhere else. These checks keep a
 * component from picking a colour of its own:
 *
 *   - the old per-feature status colours (--mm-success, --mm-status-healthy-text, text-mm-status-failed-text, ...)
 *     may only appear in the files listed below. The list is a ratchet: it can only get shorter. A file that
 *     is migrated must come off it, and a file that is not on it must not start using them.
 *   - a class that has been replaced by a status meaning is listed in RETIRED_STATUS_CLASSES and may not return.
 *   - a stylesheet other than the tokens writes no hex colour, and no markup uses a raw palette colour.
 *
 * A file that uses one of the old colours for something that is not a status (a chart line in the brand blue, a
 * button that removes things) moves from MIGRATE_ME to NOT_A_STATUS with the reason.
 */

const MIGRATE_ME: readonly string[] = ["styles/weir-panels.css"];

const NOT_A_STATUS: Readonly<Record<string, string>> = {
  "lib/ui/mm-control-roles.ts":
    "the danger button role: a button that removes or overrides, not a status",
  "styles/weir-sidebar-nav.css":
    "the sign-out menu item is a danger action, not a status",
};

const RETIRED_STATUS_CLASSES: readonly string[] = [
  "mm-conn__dot",
  "mm-conn--ok",
  "mm-conn--slow",
  "mm-conn--down",
  "mm-conn--untested",
  "mm-conn--asking",
  "mm-conn--answered",
  "mm-conn--failed",
  "mm-ctable__bar--",
  "mm-status-text--",
  "mm-auth-title--",
  "mm-auth-banner--",
  "mm-startup__dot--",
  "mm-sidebar-link-badge--",
  "READINESS_CLASSES",
  "updateStatusTone",
  "mm-protection--attention",
  "mm-cleanup-what--warn",
  "toneClass",
  "mm-sy-check__dot",
  "mm-sy-check--ok",
  "mm-sy-check--bad",
  "mm-sy-check--warn",
  "mm-sy-check--idle",
  "mm-sy-check--note",
  "mm-sy-area__dot",
  "mm-sy-task__dot",
  "mm-sy-task__mark",
  "mm-sy-task__running",
  "mm-sy-task--running",
  "mm-sy-task--ok",
  "mm-sy-task--failed",
  "mm-sy-task--never",
  "mm-sy-log--error",
  "mm-sy-log--warning",
  "mm-sy-log--info",
  "mm-sy-log__level--",
  "mm-sy-tool__mark--",
  "mm-sy-backup__dot",
  "mm-sy-note--bad",
  "mm-sy-ring--need",
  "mm-sy-tag--warning",
  "mm-sy-drive--low",
  "mm-log-dot",
  "mm-log-row--error",
  "mm-log-row--warning",
  "data-rag",
  "STATUS_RAG",
  "mm-library-scan__dot",
  "GROUP_TONE",
  "TONE_CLASS",
  "mm-history-saved",
  "mm-story-step--",
  "mm-direct-play__device--",
  "mm-direct-play__mark",
  "mm-activity-processing--",
  "mm-activity-remux-detail__chip--",
  "mm-pipe__status--",
  "mm-pipe__card--failed",
  "mm-pipe__card--rejected",
  "--pipe-success",
  "--pipe-warning",
  "--pipe-destructive",
  "mm-shelf__status--",
  "cs-legend__dot",
  "mm-stream__icon--",
  "CardTone",
  "ActivityTone",
];

const RAW_HEX_IN_STYLESHEETS: readonly string[] = [];

/** The files that define or map the colours: they are the one place a colour is chosen. */
const COLOUR_DEFINITIONS = new Set([
  "index.css",
  "styles/weir-tokens.css",
  "styles/weir-status.css",
]);

const OLD_STATUS_COLOUR =
  /var\(--mm-(?:status-(?:healthy|warning|failed|info)-(?:text|bg)|success|warning|destructive|info|warning-(?:border|bg|text))\)|\b(?:text|bg|border)-mm-(?:status-(?:healthy|warning|failed|info)-(?:text|bg)|success|warning|destructive|info)\b/;
const RAW_PALETTE_UTILITY =
  /\b(?:text|bg|border|ring|fill|stroke)-(?:red|green|emerald|amber|yellow|orange|rose|sky|blue|teal|lime|cyan)-\d{2,3}\b/;
const HEX_COLOUR = /#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3}\b/;
const CSS_COMMENT = /\/\*[\s\S]*?\*\//g;

type SourceFile = { path: string; text: string };

const RAW_SOURCES = import.meta.glob<string>(
  [
    "/src/**/*.{css,ts,tsx}",
    "!/src/**/*.test.{ts,tsx}",
    "!/src/lib/api/generated/**",
  ],
  { query: "?raw", import: "default", eager: true },
);

const SOURCES: SourceFile[] = Object.entries(RAW_SOURCES).map(
  ([key, text]) => ({
    path: key.replace("/src/", ""),
    text: text.replace(CSS_COMMENT, " "),
  }),
);
const stylesheets = () =>
  SOURCES.filter(
    (file) => file.path.endsWith(".css") && !COLOUR_DEFINITIONS.has(file.path),
  );
const usingOldColour = () =>
  SOURCES.filter(
    (file) =>
      !COLOUR_DEFINITIONS.has(file.path) && OLD_STATUS_COLOUR.test(file.text),
  ).map((file) => file.path);

describe("status colours come from status meanings", () => {
  it("only the files on the migration list still use an old status colour", () => {
    const allowed = new Set([...MIGRATE_ME, ...Object.keys(NOT_A_STATUS)]);

    expect(usingOldColour().filter((file) => !allowed.has(file))).toEqual([]);
  });

  it("a file that no longer uses an old status colour is off the migration list", () => {
    const using = new Set(usingOldColour());

    expect(
      [...MIGRATE_ME, ...Object.keys(NOT_A_STATUS)].filter(
        (file) => !using.has(file),
      ),
    ).toEqual([]);
  });

  it("no retired status class is used again", () => {
    const returned = RETIRED_STATUS_CLASSES.flatMap((name) =>
      SOURCES.filter((file) => file.text.includes(name)).map(
        (file) => `${file.path}: ${name}`,
      ),
    );

    expect(returned).toEqual([]);
  });

  it("no stylesheet but the tokens writes a hex colour", () => {
    const writing = stylesheets()
      .filter((file) => HEX_COLOUR.test(file.text.replace(/url\([^)]*\)/g, "")))
      .map((file) => file.path);

    expect(writing).toEqual([...RAW_HEX_IN_STYLESHEETS]);
  });

  it("no markup uses a raw palette colour", () => {
    const using = SOURCES.filter((file) =>
      RAW_PALETTE_UTILITY.test(file.text),
    ).map((file) => file.path);

    expect(using).toEqual([]);
  });
});
