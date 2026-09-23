/**
 * What happened to one file, read from the Activity entry written when Weir finished with it: a
 * download's `processing.file_remux_pass_completed`, or a library file's `library.file_cleaned`.
 * Live's "Just finished" list reads these (docs/exec-plans/active/live-and-library.md). Every number
 * comes from the entry itself; nothing is estimated here.
 */
import type { ActivityEventItem } from "../api/types";

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

type Detail = Record<string, unknown>;

function parse(detail: string | null | undefined): Detail | null {
  if (!detail?.trim().startsWith("{")) return null;
  try {
    const value = JSON.parse(detail) as unknown;
    return value && typeof value === "object" ? (value as Detail) : null;
  } catch {
    return null;
  }
}

function text(value: unknown): string | null {
  if (value == null) return null;
  const s = String(value).trim();
  return s ? s : null;
}

function count(value: unknown): number {
  return Array.isArray(value) ? value.length : 0;
}

function number(value: unknown): number | null {
  if (typeof value === "number" && Number.isFinite(value)) return value;
  if (
    typeof value === "string" &&
    value.trim() &&
    Number.isFinite(Number(value))
  )
    return Number(value);
  return null;
}

/** The finished file an Activity entry describes, or null for any other kind of entry. */
export function finishedFileFromEvent(
  ev: ActivityEventItem,
): FinishedFile | null {
  const detail = parse(ev.detail);
  if (ev.event_type === REMUX_PASS_COMPLETED_EVENT) {
    const outcome = text(detail?.outcome) ?? "";
    const passed = detail?.pass_through_unchanged === true;
    const failed = outcome.startsWith("failed") || detail?.ok === false;
    const already =
      outcome === "live_skipped_not_required" ||
      detail?.remux_required === false;
    const source = number(detail?.source_size_bytes);
    const output = number(detail?.output_size_bytes);
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
        text(detail?.relative_media_path) ?? ev.relative_path ?? "this file",
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
        text(detail?.relative_path) ?? ev.relative_path ?? "this file",
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
