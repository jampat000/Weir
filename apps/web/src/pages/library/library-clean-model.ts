/** The words around a clean: what the confirmation asks, and what the clean did afterwards. */
import { formatBytes } from "../../lib/format/bytes";
import { baseName } from "../../lib/format/path";
import type {
  LibraryCleanResult,
  LibraryConfirmationRequired,
  LibraryFile,
} from "../../lib/processing/library-mode-api";
import { plural } from "../../lib/ui/mm-plural";

/** What cannot be undone, in the words every clean confirmation and the file panel use. */
export const REMOVAL_IS_FINAL =
  "Removed tracks are gone for good: the only way to get one back is to download the title again.";

/** "4 tracks will come out of 1 file, giving back about 318 MB." */
export function confirmationSummary(
  asked: LibraryConfirmationRequired,
): string {
  const back =
    asked.estimated_bytes_saved > 0
      ? `, giving back about ${formatBytes(asked.estimated_bytes_saved)}`
      : "";
  return `${plural(asked.tracks_count, "track", "tracks")} will come out of ${plural(asked.files_count, "file", "files")}${back}.`;
}

function names(paths: string[]): string {
  return paths.map((path) => baseName(path)).join(", ");
}

/**
 * What a clean did, a sentence each: what was queued, what you had said to leave alone, and what Weir skipped
 * on its own (still seeding, or anything else its checks refused). `known` is the files the page has, which
 * say why a skipped one was skipped.
 */
export function outcomeLines(
  outcome: LibraryCleanResult,
  known: readonly LibraryFile[],
): string[] {
  const byPath = new Map(known.map((file) => [file.path, file]));
  const leftAlone: string[] = [];
  const seeding: string[] = [];
  const other: string[] = [];
  for (const path of outcome.skipped_paths) {
    const file = byPath.get(path);
    if (file?.leave_alone) leftAlone.push(path);
    else if (file?.problem_kind === "seeding") seeding.push(path);
    else other.push(path);
  }

  const lines = [
    outcome.queued > 0
      ? `${plural(outcome.queued, "file is", "files are")} queued to clean.`
      : "Nothing was queued.",
  ];
  if (leftAlone.length > 0) {
    lines.push(`Left alone, so not cleaned: ${names(leftAlone)}.`);
  }
  if (seeding.length > 0) {
    const verb = seeding.length === 1 ? "is" : "are";
    lines.push(
      `Weir skipped ${seeding.length.toLocaleString()} that ${verb} still seeding: ${names(seeding)}.`,
    );
  }
  if (other.length > 0) {
    lines.push(
      `Weir skipped ${other.length.toLocaleString()}: ${names(other)}.`,
    );
  }
  return lines;
}
