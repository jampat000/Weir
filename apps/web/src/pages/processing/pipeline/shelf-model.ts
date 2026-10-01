import type { FinishedFile } from "../../../lib/activity/processing-outcome";
import { formatBytes } from "../../../lib/format/bytes";
import { parseAppTime } from "../../../lib/ui/mm-format-date";
import type { Filter } from "../processing-filter";
import { prettyName } from "../processing-model";
import {
  ago,
  finishedLine,
  isJustNow,
  removedTrackWords,
} from "../processing-words";
import { shownBy } from "./pipeline-cards";

/** The workflow's name for a file whose entry names no workflow. */
const UNKNOWN_WORKFLOW = "Workflow";

/** The most tiles the shelf holds: the room it has decides how many of them show. */
export const SHELF_LIMIT = 12;

/** One finished file as a tile on the Just finished shelf. */
export type ShelfTile = {
  key: string;
  path: string;
  title: string;
  /** The workflow's name, which the tile is tinted for. */
  workflow: string;
  /** Whether the workflow is known: an unknown one is tinted but not named on the tile. */
  workflowKnown: boolean;
  /** What the file shrank by, "−318 MB"; nothing when no sizes were recorded. */
  saved: string | null;
  /** The same without its sign, "318 MB", for sentences. */
  savedAmount: string | null;
  /** What was done to it, in a few words: "2 audio, 4 subtitles removed". */
  what: string;
  ago: string;
  /** It finished a moment ago, so the tile wears the ring. */
  fresh: boolean;
  /** The whole sentence, for the tooltip. */
  detail: string;
  item: FinishedFile;
};

/** What Just finished adds up to: today's counts and the tiles to show, newest first. */
export type Shelf = {
  /** Files that finished today, and what they add up to. */
  today: number;
  savedBytes: number;
  /** Nothing finished today, so the tiles are the latest from before. */
  latest: boolean;
  tiles: ShelfTile[];
};

export type ShelfScope = {
  filter: Filter;
  /** Only this workflow's files; every workflow's when null. */
  workflowId: number | null;
  now: number;
};

const SHORT_WHAT: Partial<Record<FinishedFile["kind"], string>> = {
  already: "Already right",
  passed: "Passed through unchanged",
  rejected: "Rejected",
  failed: "Could not be finished",
};

/** What was done to a file, in a few words. */
function whatWasDone(item: FinishedFile): string {
  const removed = removedTrackWords(item);
  if (removed.length > 0) return `${removed.join(", ")} removed`;
  return (
    SHORT_WHAT[item.kind] ??
    (item.source === "library" ? "Cleaned in place" : "Cleaned")
  );
}

function isToday(iso: string, now: number): boolean {
  const at = parseAppTime(iso);
  return (
    at != null && new Date(at).toDateString() === new Date(now).toDateString()
  );
}

function tileOf(
  item: FinishedFile,
  now: number,
  names: ReadonlyMap<number, string>,
): ShelfTile {
  const workflow =
    item.libraryId == null ? undefined : names.get(item.libraryId);
  return {
    key: String(item.id),
    path: item.relativePath,
    title: prettyName(item.relativePath),
    workflow: workflow || UNKNOWN_WORKFLOW,
    workflowKnown: Boolean(workflow),
    saved: item.savedBytes ? `−${formatBytes(item.savedBytes)}` : null,
    savedAmount: item.savedBytes ? formatBytes(item.savedBytes) : null,
    what: whatWasDone(item),
    ago: ago(item.finishedAt, now),
    fresh: isJustNow(item.finishedAt, now),
    detail: finishedLine(item),
    item,
  };
}

/**
 * The shelf for the files the page's filter and workflow let through: today's files if there are any,
 * and otherwise the latest from before.
 */
export function shelfOf(
  items: readonly FinishedFile[],
  scope: ShelfScope,
  names: ReadonlyMap<number, string>,
): Shelf {
  const wanted = items.filter(
    (item) =>
      shownBy(scope.filter, item) &&
      (scope.workflowId === null || item.libraryId === scope.workflowId),
  );
  const today = wanted.filter((item) => isToday(item.finishedAt, scope.now));
  const latest = today.length === 0;
  return {
    today: today.length,
    savedBytes: today.reduce((sum, item) => sum + (item.savedBytes ?? 0), 0),
    latest,
    tiles: (latest ? wanted : today)
      .slice(0, SHELF_LIMIT)
      .map((item) => tileOf(item, scope.now, names)),
  };
}

/** "12 today · 41.20 GB saved": what the shelf's files finished today. */
export function todayWords(shelf: Shelf): string {
  if (shelf.latest) return "Nothing yet today · latest arrivals";
  const saved =
    shelf.savedBytes > 0 ? ` · ${formatBytes(shelf.savedBytes)} saved` : "";
  return `${shelf.today} today${saved}`;
}
