import type {
  ProcessingFile,
  ProcessingFileLogEntry,
  ProcessingFileStatus,
} from "../../lib/processing/files-api";
import { processingStreamLanguageLabel } from "../../lib/processing/stream-language-options";
import { parseAppDate } from "../../lib/ui/mm-format-date";

/** A server time as epoch ms. Its times carry no zone and are UTC. */
export function serverMs(iso: string): number {
  return parseAppDate(iso).getTime();
}

/** The five ways History sorts a file, in the order the chips read. */
export type HistoryGroup = "all" | "working" | "finished" | "needs" | "failed";

export const HISTORY_GROUPS: { id: HistoryGroup; label: string }[] = [
  { id: "all", label: "All" },
  { id: "working", label: "In progress" },
  { id: "finished", label: "Finished" },
  { id: "needs", label: "Needs you" },
  { id: "failed", label: "Failed" },
];

const WORKING: readonly ProcessingFileStatus[] = [
  "unprocessed",
  "processing",
  "on_hold",
  "out_of_schedule",
];
const FINISHED: readonly ProcessingFileStatus[] = [
  "processed",
  "passed_through",
];
const FAILED: readonly ProcessingFileStatus[] = [
  "processing_failed",
  "rejected",
  "skipped",
];

/**
 * Where a file belongs. A file held after repeated failures, or one its media manager still has, waits on a
 * person, so it is "Needs you" whatever its status says; a library that is off or a cancelled pass is neither
 * working nor finished, so it only shows under All.
 */
export function historyGroupOf(file: ProcessingFile): HistoryGroup | null {
  if (file.quarantined || file.status === "blocked_upstream") return "needs";
  if (WORKING.includes(file.status)) return "working";
  if (FINISHED.includes(file.status)) return "finished";
  if (FAILED.includes(file.status)) return "failed";
  return null;
}

export function inGroup(file: ProcessingFile, group: HistoryGroup): boolean {
  return group === "all" || historyGroupOf(file) === group;
}

/** The file's own name, without the folders it sits in. */
export function fileName(relativePath: string): string {
  const parts = relativePath.split(/[\\/]/);
  return parts[parts.length - 1] || relativePath;
}

/** Newest change first, so what just happened is at the top. */
export function newestFirst(files: ProcessingFile[]): ProcessingFile[] {
  return [...files].sort(
    (a, b) => serverMs(b.updated_at) - serverMs(a.updated_at),
  );
}

/** A size in the units people read, never more than one decimal. */
export function sizeWords(bytes: number | null | undefined): string | null {
  if (typeof bytes !== "number" || !Number.isFinite(bytes) || bytes < 0) {
    return null;
  }
  const units = ["B", "KB", "MB", "GB", "TB"];
  let size = bytes;
  let unit = 0;
  while (size >= 1024 && unit < units.length - 1) {
    size /= 1024;
    unit += 1;
  }
  return `${size >= 100 || unit === 0 ? size.toFixed(0) : size.toFixed(1)} ${units[unit]}`;
}

/** "just now", "6 min ago", "2 h ago", "3 days ago". */
export function agoWords(iso: string | null | undefined, now: number): string {
  if (!iso) return "";
  const seconds = Math.max(0, Math.round((now - serverMs(iso)) / 1000));
  if (seconds < 60) return "just now";
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 48) return `${hours} h ago`;
  return `${Math.round(hours / 24)} days ago`;
}

/** How long a pass took, in the words a person uses. */
export function tookWords(seconds: number | null | undefined): string | null {
  if (typeof seconds !== "number" || !Number.isFinite(seconds) || seconds < 0) {
    return null;
  }
  const whole = Math.round(seconds);
  if (whole < 60) return `${whole} s`;
  const minutes = Math.floor(whole / 60);
  if (minutes < 60) return `${minutes} min ${whole % 60} s`;
  return `${Math.floor(minutes / 60)} h ${minutes % 60} min`;
}

/** One track as History lists it: what it is, and what happened to it. */
export type HistoryTrack = {
  kind: "Audio" | "Subtitle" | "Other";
  what: string;
  kept: boolean;
  /** Why it went, in the planner's own words. Empty for a kept track. */
  why: string;
};

function textOf(value: unknown): string {
  return typeof value === "string" ? value.trim() : "";
}

function listOf(value: unknown): string[] {
  return Array.isArray(value)
    ? value.filter((v): v is string => typeof v === "string" && v.trim() !== "")
    : [];
}

/** The planner's "a · b · c" lines, one entry per track; the dash it writes for "none" is no track. */
function splitLine(line: unknown): string[] {
  return textOf(line)
    .split(" · ")
    .map((part) => part.trim())
    .filter(
      (part) => part !== "" && part !== "—" && part !== "-" && part !== "None",
    );
}

/** The bibliographic spellings some files carry, mapped to the ones Weir's language list uses. */
const LANGUAGE_ALIASES: Record<string, string> = {
  ger: "deu",
  fra: "fre",
  chi: "zho",
  dut: "nld",
  cze: "ces",
  gre: "ell",
  rum: "ron",
  may: "msa",
};

const CODECS: Record<string, string> = {
  eac3: "E-AC-3",
  ac3: "AC-3",
  aac: "AAC",
  dts: "DTS",
  truehd: "TrueHD",
  flac: "FLAC",
  opus: "Opus",
  mp3: "MP3",
  pcm_s16le: "PCM",
  pcm_s24le: "PCM",
};

const CHANNELS: Record<string, string> = {
  "1": "mono",
  "2": "2.0",
  "6": "5.1",
  "8": "7.1",
};

/**
 * The planner describes a track the way ffprobe does ("fre eac3 6 ch (stream 2)"). Put it the way the
 * rest of History reads ("French 5.1 E-AC-3"): language named, channels as a layout, codec spelled out,
 * stream numbers dropped.
 */
export function readableTrack(text: string): string {
  const language = (code: string) =>
    processingStreamLanguageLabel(
      LANGUAGE_ALIASES[code.toLowerCase()] ?? code.toLowerCase(),
    );
  return text
    .replace(/\s*\(stream \d+\)/gi, "")
    .replace(/\b[a-z]{3}\b(?=\s+[a-z0-9_]+\s+\d+\s*ch\b)/gi, language)
    .replace(
      /\b([a-z0-9_]+)\s+(\d+)\s*ch\b/gi,
      (_, codec: string, ch: string) =>
        `${CHANNELS[ch] ?? `${ch} channels`} ${CODECS[codec.toLowerCase()] ?? codec.toUpperCase()}`,
    )
    .replace(/^[a-z]{3}$/, language)
    .trim();
}

/** The planner's reason, with any track it names made readable and the first letter raised. */
function readableWhy(text: string): string {
  const why = readableTrack(text.trim());
  return why ? why[0].toUpperCase() + why.slice(1) : "";
}

/**
 * A removal line reads "English 2.0 (commentary excluded — remove commentary enabled)" or
 * "fre eac3 6 ch (stream 2): removed (not selected — …)". Split it into the track and the reason.
 */
function splitRemoval(line: string): { what: string; why: string } {
  const clean = line.replace(/\s*\(stream \d+\)/gi, "");
  const colon = clean.match(/^(.*?):\s*removed\s*\((.*)\)\s*$/i);
  if (colon) {
    return { what: readableTrack(colon[1]), why: readableWhy(colon[2]) };
  }
  const paren = clean.match(/^(.*?)\s*\((.*)\)\s*$/);
  if (paren) {
    return { what: readableTrack(paren[1]), why: readableWhy(paren[2]) };
  }
  return { what: readableTrack(clean), why: "" };
}

/**
 * Every track the pass kept and removed, from the record it saved. Kept tracks come from the "after" lines,
 * removed ones from the removal lists with the planner's reason; extras (cover images, attachments) are
 * listed when the pass took them out.
 */
export function tracksFromRecord(
  detail: Record<string, unknown>,
): HistoryTrack[] {
  const tracks: HistoryTrack[] = [];
  for (const what of splitLine(detail.audio_after)) {
    tracks.push({ kind: "Audio", what, kept: true, why: "" });
  }
  for (const line of listOf(detail.removed_audio)) {
    tracks.push({ kind: "Audio", ...splitRemoval(line), kept: false });
  }
  for (const what of splitLine(detail.subs_after)) {
    tracks.push({ kind: "Subtitle", what, kept: true, why: "" });
  }
  for (const line of listOf(detail.removed_subtitles)) {
    tracks.push({ kind: "Subtitle", ...splitRemoval(line), kept: false });
  }
  for (const line of [
    ...listOf(detail.removed_images),
    ...listOf(detail.removed_attachments),
  ]) {
    tracks.push({ kind: "Other", ...splitRemoval(line), kept: false });
  }
  return tracks;
}

/** Before, after and saved, when the record has both sizes. */
export function sizesFromRecord(detail: Record<string, unknown>): {
  before: number;
  after: number;
  saved: number;
} | null {
  const before = detail.source_size_bytes;
  const after = detail.output_size_bytes;
  if (typeof before !== "number" || typeof after !== "number") return null;
  if (before <= 0 || after <= 0) return null;
  return { before, after, saved: Math.max(0, before - after) };
}

/** The newest record that describes a pass over the file, skipping the cleanup notes some passes add. */
export function latestPass(
  entries: ProcessingFileLogEntry[],
): ProcessingFileLogEntry | null {
  return (
    entries.find(
      (entry) =>
        entry.detail.audio_after !== undefined ||
        entry.detail.outcome !== undefined,
    ) ?? null
  );
}
