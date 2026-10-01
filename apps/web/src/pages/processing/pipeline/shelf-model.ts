import type { FinishedFile } from "../../../lib/activity/processing-outcome";
import { formatBytes } from "../../../lib/format/bytes";
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
  workflow: string;
  /** What the file shrank by, "−318 MB"; nothing when no sizes were recorded. */
  saved: string | null;
  /** What was done to it, in a few words: "2 audio, 4 subtitles removed". */
  what: string;
  ago: string;
  /** It finished a moment ago, so the tile wears the ring. */
  fresh: boolean;
  /** The whole sentence, for the tooltip. */
  detail: string;
  item: FinishedFile;
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

/**
 * The tiles of the shelf: the newest finished files the page filter lets through. `workflowNames` says which
 * workflow each file's tile is tinted for.
 */
export function shelfTiles(
  items: readonly FinishedFile[],
  filter: Filter,
  now: number,
  workflowNames: ReadonlyMap<number, string>,
): ShelfTile[] {
  return items
    .filter((item) => shownBy(filter, item))
    .slice(0, SHELF_LIMIT)
    .map((item) => ({
      key: String(item.id),
      path: item.relativePath,
      title: prettyName(item.relativePath),
      workflow:
        (item.libraryId != null && workflowNames.get(item.libraryId)) ||
        UNKNOWN_WORKFLOW,
      saved: item.savedBytes ? `−${formatBytes(item.savedBytes)}` : null,
      what: whatWasDone(item),
      ago: ago(item.finishedAt, now),
      fresh: isJustNow(item.finishedAt, now),
      detail: finishedLine(item),
      item,
    }));
}
