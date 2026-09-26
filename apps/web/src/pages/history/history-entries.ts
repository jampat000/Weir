import type {
  ProcessingFile,
  ProcessingFileStatus,
} from "../../lib/processing/files-api";
import type { LibraryClean } from "../../lib/processing/library-cleans-api";
import { serverMs } from "./history-model";

/**
 * One row in History: a new download Weir processed, or a file it cleaned where it sits in a library (#695). Both are
 * listed together, so History is the one place to look for what happened to a file.
 */
export type HistoryEntry =
  | { kind: "download"; key: string; file: ProcessingFile }
  | { kind: "library_clean"; key: string; clean: LibraryClean };

export const downloadEntry = (file: ProcessingFile): HistoryEntry => ({
  kind: "download",
  key: `download-${file.id}`,
  file,
});

export const cleanEntry = (clean: LibraryClean): HistoryEntry => ({
  kind: "library_clean",
  key: `clean-${clean.id}`,
  clean,
});

/**
 * The ways History sorts an entry, in the order the chips read. "kept" is not an entry group at all — a kept
 * file has no `files` row left to be one (#786 review of #785) — but it lives in the same chip row and count
 * pattern, since removing a title to keep it is still something History did with it.
 */
export type HistoryGroup =
  "all" | "working" | "finished" | "needs" | "skipped" | "failed" | "kept";

export const HISTORY_GROUPS: { id: HistoryGroup; label: string }[] = [
  { id: "all", label: "All" },
  { id: "working", label: "In progress" },
  { id: "finished", label: "Finished" },
  { id: "needs", label: "Needs you" },
  { id: "skipped", label: "Skipped" },
  { id: "failed", label: "Failed" },
  { id: "kept", label: "Kept" },
];

const WORKING: readonly ProcessingFileStatus[] = [
  "unprocessed",
  "processing",
  "out_of_schedule",
];
const NEEDS: readonly ProcessingFileStatus[] = ["on_hold", "blocked_upstream"];
const FINISHED: readonly ProcessingFileStatus[] = [
  "processed",
  "passed_through",
];
const FAILED: readonly ProcessingFileStatus[] = [
  "processing_failed",
  "rejected",
];

const CLEAN_GROUPS: Record<LibraryClean["outcome"], HistoryGroup> = {
  cleaned: "finished",
  skipped: "skipped",
  failed: "failed",
};

/**
 * Where a download belongs. A file held after repeated failures, on hold, or one its media manager still has waits on
 * a person, so it is "Needs you"; a skip is Weir deciding a file is not for it, which is not a failure. A library that
 * is off or a cancelled pass is neither working nor finished, so it only shows under All.
 */
export function historyGroupOf(file: ProcessingFile): HistoryGroup | null {
  if (file.quarantined || NEEDS.includes(file.status)) return "needs";
  if (WORKING.includes(file.status)) return "working";
  if (FINISHED.includes(file.status)) return "finished";
  if (file.status === "skipped") return "skipped";
  if (FAILED.includes(file.status)) return "failed";
  return null;
}

export function entryGroup(entry: HistoryEntry): HistoryGroup | null {
  return entry.kind === "download"
    ? historyGroupOf(entry.file)
    : CLEAN_GROUPS[entry.clean.outcome];
}

export function inGroup(entry: HistoryEntry, group: HistoryGroup): boolean {
  return group === "all" || entryGroup(entry) === group;
}

/** When the entry last changed: a download's last state change, or when the clean finished. */
export function entryTime(entry: HistoryEntry): string {
  return entry.kind === "download"
    ? entry.file.updated_at
    : entry.clean.recorded_at;
}

export function entryPath(entry: HistoryEntry): string {
  return entry.kind === "download"
    ? entry.file.relative_path
    : entry.clean.relative_path;
}

/** Both kinds together, newest change first, so what just happened is at the top. */
export function historyEntries(
  files: ProcessingFile[],
  cleans: LibraryClean[],
): HistoryEntry[] {
  return [...files.map(downloadEntry), ...cleans.map(cleanEntry)].sort(
    (a, b) => serverMs(entryTime(b)) - serverMs(entryTime(a)),
  );
}

/** The failed downloads that "Try again" can queue: a failed library clean is cleaned again from Library. */
export function retryableFailures(entries: HistoryEntry[]): ProcessingFile[] {
  return entries.flatMap((entry) =>
    entry.kind === "download" && entry.file.status === "processing_failed"
      ? [entry.file]
      : [],
  );
}
