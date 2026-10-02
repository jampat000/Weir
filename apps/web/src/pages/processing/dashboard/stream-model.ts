/**
 * The Activity stream: what Weir just did, as one plain line each. File events are worded from the file's own
 * outcome; the others from the log's own title. Routine housekeeping is counted rather than listed, and a
 * pass's live progress frames are not news at all.
 */
import {
  eventDisplay,
  type ActivityTone,
} from "../../../lib/activity/activity-display";
import { asString, parseActivityDetail } from "../../../lib/activity/detail";
import {
  FILE_PROGRESS_EVENT,
  HANDBACK_OUTCOME_EVENT,
  LIBRARY_FILE_CLEANED_EVENT,
  REMUX_PASS_COMPLETED_EVENT,
} from "../../../lib/activity/event-types";
import {
  finishedFileFromEvent,
  type FinishedFile,
} from "../../../lib/activity/processing-outcome";
import type { ActivityEventItem } from "../../../lib/api/types";
import { parseAppTime } from "../../../lib/ui/mm-format-date";
import { prettyName } from "../processing-model";
import { ago, finishedNote } from "../processing-words";

/** The most lines the stream keeps; the box scrolls inside. */
export const STREAM_ROWS = 20;

const LOG_PATH = "/system?tab=logs";
const HOUR_MS = 3_600_000;
const DAY_HOURS = 24;

/** A line is plain words with the file's name in bold. */
export type StreamPart = string | { bold: string };

export type StreamRow = {
  key: string;
  /** The newest event's id, for telling a line that has arrived from one already on screen. */
  id: number;
  parts: StreamPart[];
  /** What kind of thing it was, and the detail that goes with it. */
  note: string;
  tone: ActivityTone;
  at: string;
  to: string;
  /** How many identical neighbouring lines this one stands for. */
  times: number;
};

export type Stream = {
  rows: StreamRow[];
  /** Housekeeping entries left out: queue checks, temporary-file sweeps. */
  routine: number;
};

const OUTCOME_WORDS: Record<
  FinishedFile["kind"],
  { tail: string; tone: ActivityTone }
> = {
  cleaned: { tail: " cleaned", tone: "success" },
  already: { tail: " was already clean", tone: "info" },
  passed: { tail: " passed through", tone: "warning" },
  rejected: { tail: " was rejected", tone: "warning" },
  failed: { tail: " couldn't finish", tone: "error" },
};

function historyPath(relativePath: string): string {
  return `/history?q=${encodeURIComponent(relativePath)}`;
}

function finishedRow(ev: ActivityEventItem, file: FinishedFile): StreamRow {
  const outcome = OUTCOME_WORDS[file.kind];
  const library = file.source === "library" && file.kind === "cleaned";
  return {
    key: `file-${ev.id}`,
    id: ev.id,
    parts: [
      { bold: prettyName(file.relativePath) },
      library ? " cleaned in place" : outcome.tail,
    ],
    note: finishedNote(file),
    tone: outcome.tone,
    at: ev.created_at,
    to: historyPath(file.relativePath),
    times: 1,
  };
}

/** "Radarr imported <name>", or "Radarr did not import <name>": a media manager's word on a handed-back copy. */
function handbackRow(ev: ActivityEventItem): StreamRow {
  const detail = parseActivityDetail(ev.detail);
  const imported = asString(detail?.outcome) === "imported";
  const by = asString(detail?.outcome_by) ?? "A media manager";
  const path = asString(detail?.relative_media_path) ?? ev.relative_path ?? "";
  return {
    key: `handback-${ev.id}`,
    id: ev.id,
    parts: [
      `${by} ${imported ? "imported" : "did not import"} `,
      { bold: prettyName(path) },
    ],
    note: "Handed back",
    tone: imported ? "success" : "warning",
    at: ev.created_at,
    to: path ? historyPath(path) : LOG_PATH,
    times: 1,
  };
}

function logRow(ev: ActivityEventItem): StreamRow {
  const display = eventDisplay(ev);
  const file = ev.relative_path;
  return {
    key: `log-${ev.id}`,
    id: ev.id,
    parts: [display.title],
    note: display.summary,
    tone: display.tone,
    at: ev.created_at,
    to: file ? historyPath(file) : LOG_PATH,
    times: 1,
  };
}

function rowOf(ev: ActivityEventItem): StreamRow | null {
  switch (ev.event_type) {
    case REMUX_PASS_COMPLETED_EVENT:
    case LIBRARY_FILE_CLEANED_EVENT: {
      const file = finishedFileFromEvent(ev);
      return file ? finishedRow(ev, file) : null;
    }
    case HANDBACK_OUTCOME_EVENT:
      return handbackRow(ev);
    default:
      return logRow(ev);
  }
}

const sentence = (row: StreamRow): string =>
  row.parts
    .map((part) => (typeof part === "string" ? part : part.bold))
    .join("");

/** The newest events as lines, newest first; neighbours that say the same thing are one line with a count. */
export function buildStream(items: readonly ActivityEventItem[]): Stream {
  const rows: StreamRow[] = [];
  let routine = 0;
  for (const ev of items) {
    if (ev.event_type === FILE_PROGRESS_EVENT) continue;
    if (eventDisplay(ev).compact) {
      routine += 1;
      continue;
    }
    const row = rowOf(ev);
    if (!row) continue;
    const previous = rows.at(-1);
    if (previous && sentence(previous) === sentence(row)) {
      previous.times += 1;
    } else {
      rows.push(row);
    }
  }
  return { rows: rows.slice(0, STREAM_ROWS), routine };
}

/** When a line happened: "4 min ago" for the first day, then the date. */
export function streamWhen(iso: string, now: number): string {
  const at = parseAppTime(iso);
  if (at === null) return "";
  if ((now - at) / HOUR_MS < DAY_HOURS) return ago(iso, now);
  return new Date(at).toLocaleDateString("en-US", {
    month: "short",
    day: "numeric",
  });
}
