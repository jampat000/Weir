/**
 * What happened to one file, read from the Activity entry written when Weir finished with it: a
 * download's `processing.file_remux_pass_completed`, or a library file's `library.file_cleaned`.
 * Live's "Just finished" list reads these (docs/archive/live-and-library.md). Every number
 * comes from the entry itself; nothing is estimated here.
 */
import type { ActivityEventItem } from "../api/types";
import { asNumber, asString, parseActivityDetail } from "./detail";

export const REMUX_PASS_COMPLETED_EVENT =
  "processing.file_remux_pass_completed";
export const LIBRARY_FILE_CLEANED_EVENT = "library.file_cleaned";

export type FinishedKind = "cleaned" | "already" | "passed" | "failed";

export type FinishedFile = {
  id: number;
  /** Where the file came from: a new download, or a file already in a library. */
  source: "download" | "library";
  kind: FinishedKind;
  relativePath: string;
  libraryId: number | null;
  /** Bytes the file shrank by, when the entry recorded both sizes. */
  savedBytes: number | null;
  removedAudio: number;
  removedSubtitles: number;
  /** The entry's own sentence, for kinds that have one worth showing as it is. */
  sentence: string | null;
  finishedAt: string;
};

function count(value: unknown): number {
  return Array.isArray(value) ? value.length : 0;
}

/** The finished file an Activity entry describes, or null for any other kind of entry. */
export function finishedFileFromEvent(
  ev: ActivityEventItem,
): FinishedFile | null {
  const detail = parseActivityDetail(ev.detail);
  if (ev.event_type === REMUX_PASS_COMPLETED_EVENT) {
    const outcome = asString(detail?.outcome) ?? "";
    const passed = detail?.pass_through_unchanged === true;
    const failed = outcome.startsWith("failed") || detail?.ok === false;
    const already =
      outcome === "live_skipped_not_required" ||
      detail?.remux_required === false;
    const source = asNumber(detail?.source_size_bytes);
    const output = asNumber(detail?.output_size_bytes);
    return {
      id: ev.id,
      source: "download",
      kind: passed
        ? "passed"
        : failed
          ? "failed"
          : already
            ? "already"
            : "cleaned",
      relativePath:
        asString(detail?.relative_media_path) ??
        ev.relative_path ??
        "this file",
      libraryId: ev.library_id ?? null,
      savedBytes:
        source != null && output != null && source > output
          ? source - output
          : null,
      removedAudio: count(detail?.removed_audio),
      removedSubtitles: count(detail?.removed_subtitles),
      sentence: null,
      finishedAt: ev.created_at,
    };
  }
  if (ev.event_type === LIBRARY_FILE_CLEANED_EVENT) {
    return {
      id: ev.id,
      source: "library",
      kind: "cleaned",
      relativePath:
        asString(detail?.relative_path) ?? ev.relative_path ?? "this file",
      libraryId: ev.library_id ?? null,
      savedBytes: null,
      removedAudio: 0,
      removedSubtitles: 0,
      sentence: ev.title,
      finishedAt: ev.created_at,
    };
  }
  return null;
}
