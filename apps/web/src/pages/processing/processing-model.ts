/**
 * Which lane each file belongs in on Live, and the words and numbers each card shows. Pure functions,
 * so the rules are tested on their own (live-model.test.ts). Every value comes from the server: file
 * states and the running pass's own progress from `GET /api/v1/processing/files`, library cleans from
 * the job queue, finished files from their Activity entries.
 */
import type { FinishedFile } from "../../lib/activity/processing-outcome";
import { formatBytes } from "../../lib/format/bytes";
import type { ProcessingFile } from "../../lib/processing/files-api";
import type { ProcessingJobInspectionRow } from "../../lib/processing/jobs-inspection/types";

export const LIBRARY_CLEAN_JOB_KIND = "processing.library.clean.v1";

export type WorkSource = "download" | "library";

export type ArrivingItem = {
  key: string;
  file: ProcessingFile;
  name: string;
  facts: string;
  note: string;
  /** When the wait ends (epoch ms), for a countdown. Null when the wait is on a writer, not a clock. */
  holdUntil: number | null;
  /** How long the whole wait is (seconds), so the ring can show how much of it has passed. */
  holdTotal: number | null;
  /** The media manager still has it (blocked upstream). */
  upstream: boolean;
};

export type WaitingItem = {
  key: string;
  source: WorkSource;
  name: string;
  facts: string;
  note: string | null;
  libraryName: string;
  file: ProcessingFile | null;
};

export type WorkingItem = {
  key: string;
  source: WorkSource;
  name: string;
  facts: string;
  libraryName: string;
  percent: number | null;
  etaSeconds: number | null;
  speed: string | null;
  removedAudio: number;
  removedSubtitles: number;
  file: ProcessingFile | null;
};

export type HandingItem = {
  key: string;
  source: WorkSource;
  name: string;
  libraryName: string;
  file: ProcessingFile | null;
};

export type Lanes = {
  arriving: ArrivingItem[];
  waiting: WaitingItem[];
  working: WorkingItem[];
  handing: HandingItem[];
  /** Files that need a person: failed, or on hold after repeated failures. */
  stuck: ProcessingFile[];
};

const EPISODE = /^(.+?)[ ._-]+(S\d{1,2}E\d{1,3}(?:-?E\d{1,3})?)(?:[ ._-]|$)/i;
const FILM = /^(.+?)[ ._-]+\(?((?:19|20)\d{2})\)?(?:[ ._-]|$)/;

function words(raw: string): string {
  return raw.replace(/[._]+/g, " ").replace(/\s+/g, " ").trim();
}

/** "The.Quiet.Harbour.S01E03.1080p.WEB-DL.mkv" reads as "The Quiet Harbour S01E03". */
export function prettyName(path: string): string {
  const base = path.split(/[\\/]/).filter(Boolean).at(-1) ?? path;
  const stem = base.replace(/\.[a-z0-9]{2,4}$/i, "");
  const episode = EPISODE.exec(stem);
  if (episode) return `${words(episode[1])} ${episode[2].toUpperCase()}`;
  const film = FILM.exec(stem);
  if (film) return `${words(film[1])} (${film[2]})`;
  return words(stem) || base;
}

/** "1080p · H264 · 2.27 GB": what the last probe measured. Missing facts are left out, never guessed. */
export function fileFacts(file: ProcessingFile): string {
  const bits: string[] = [];
  if (file.video_height) bits.push(`${file.video_height}p`);
  if (file.video_codec) bits.push(file.video_codec.toUpperCase());
  const size = formatBytes(file.size_bytes);
  if (size) bits.push(size);
  return bits.join(" · ");
}

/** The first sentence of a reason Weir wrote, which is the part that fits on a card. */
export function firstSentence(text: string | null | undefined): string {
  const t = (text ?? "").trim();
  const end = t.search(/[.!?](\s|$)/);
  return end > 0 ? t.slice(0, end + 1) : t;
}

function parseTime(value: string | null | undefined): number | null {
  if (!value) return null;
  const ms = Date.parse(
    /[zZ]|[+-]\d\d:?\d\d$/.test(value) ? value : `${value}Z`,
  );
  return Number.isNaN(ms) ? null : ms;
}

/** Seconds until a wait ends, rounded up, never negative. Null when there is no clock on it. */
export function secondsLeft(
  holdUntil: number | null,
  now: number,
): number | null {
  if (holdUntil == null) return null;
  return Math.max(0, Math.ceil((holdUntil - now) / 1000));
}

function libraryJobParts(row: ProcessingJobInspectionRow): {
  path: string;
  libraryId: number | null;
} {
  try {
    const payload = JSON.parse(row.payload_json ?? "{}") as {
      path?: unknown;
      library_id?: unknown;
    };
    return {
      path: typeof payload.path === "string" ? payload.path : "",
      libraryId:
        typeof payload.library_id === "number" ? payload.library_id : null,
    };
  } catch {
    return { path: "", libraryId: null };
  }
}

/**
 * Sorts files and library clean jobs into Live's lanes.
 *
 * - Arriving: on hold for a reason that ends by itself (a file still being written, or inside its
 *   library's minimum age), or still held by the media manager.
 * - Waiting: ready, or outside its library's hours, and waiting for a free lane.
 * - Working: a pass is writing it. Handing back: the pass has written it and is on its final checks.
 * - Stuck: failed, or on hold after repeated failures. These are what "Needs you" is made of.
 *
 * Library cleans come from the job queue: leased is working, pending is waiting. They do not report a
 * percentage yet, so their card says what is happening without a number rather than invent one.
 */
export function buildLanes(
  files: ProcessingFile[],
  libraryJobs: ProcessingJobInspectionRow[],
  libraryNames: Map<number, string>,
  minAgeByLibrary: Map<number, number>,
): Lanes {
  const lanes: Lanes = {
    arriving: [],
    waiting: [],
    working: [],
    handing: [],
    stuck: [],
  };

  for (const file of files) {
    const name = prettyName(file.relative_path);
    const facts = fileFacts(file);
    const libraryName =
      file.library_name || libraryNames.get(file.library_id) || "Library";
    const key = `file-${file.id}`;
    switch (file.status) {
      case "on_hold": {
        if (file.quarantined) {
          lanes.stuck.push(file);
          break;
        }
        const holdUntil = parseTime(file.hold_until);
        const since =
          parseTime(file.size_changed_at) ?? parseTime(file.updated_at);
        const minAge = minAgeByLibrary.get(file.library_id) ?? null;
        const holdTotal =
          holdUntil != null && since != null && holdUntil > since
            ? (holdUntil - since) / 1000
            : minAge;
        lanes.arriving.push({
          key,
          file,
          name,
          facts,
          note: firstSentence(file.status_reason),
          holdUntil,
          holdTotal,
          upstream: false,
        });
        break;
      }
      case "blocked_upstream":
        lanes.arriving.push({
          key,
          file,
          name,
          facts,
          note: file.blocked_by_connection
            ? `${file.blocked_by_connection} is still importing it.`
            : firstSentence(file.status_reason),
          holdUntil: null,
          holdTotal: null,
          upstream: true,
        });
        break;
      case "unprocessed":
      case "out_of_schedule":
        lanes.waiting.push({
          key,
          source: "download",
          name,
          facts,
          note:
            file.status === "out_of_schedule"
              ? firstSentence(file.status_reason)
              : null,
          libraryName,
          file,
        });
        break;
      case "processing": {
        if (file.progress_status === "finishing") {
          lanes.handing.push({
            key,
            source: "download",
            name,
            libraryName,
            file,
          });
          break;
        }
        lanes.working.push({
          key,
          source: "download",
          name,
          facts,
          libraryName,
          percent: file.progress_percent,
          etaSeconds: file.progress_eta_seconds,
          speed: file.progress_speed ?? null,
          removedAudio: file.progress_removed_audio?.length ?? 0,
          removedSubtitles: file.progress_removed_subtitles?.length ?? 0,
          file,
        });
        break;
      }
      case "processing_failed":
        lanes.stuck.push(file);
        break;
      default:
        break;
    }
  }

  for (const row of libraryJobs) {
    if (row.job_kind !== LIBRARY_CLEAN_JOB_KIND) continue;
    const { path, libraryId } = libraryJobParts(row);
    const libraryName =
      (libraryId != null && libraryNames.get(libraryId)) || "Library";
    const item = {
      key: `job-${row.id}`,
      source: "library" as const,
      name: prettyName(path || `Library file ${row.id}`),
      facts: "Cleaning in place",
      libraryName,
      file: null,
    };
    if (row.status === "leased") {
      lanes.working.push({
        ...item,
        percent: null,
        etaSeconds: null,
        speed: null,
        removedAudio: 0,
        removedSubtitles: 0,
      });
    } else if (row.status === "pending") {
      lanes.waiting.push({ ...item, note: null });
    }
  }

  // The file closest to its turn first; the rest keep the server's order.
  lanes.arriving.sort(
    (a, b) => (a.holdUntil ?? Infinity) - (b.holdUntil ?? Infinity),
  );
  return lanes;
}

/** Files handed back per `bucketMinutes`, oldest first, over the last `windowMinutes`. */
export function throughput(
  finished: FinishedFile[],
  now: number,
  windowMinutes = 120,
  bucketMinutes = 5,
): number[] {
  const buckets = Array.from(
    { length: Math.ceil(windowMinutes / bucketMinutes) },
    () => 0,
  );
  const start = now - windowMinutes * 60_000;
  for (const item of finished) {
    const at = parseTime(item.finishedAt);
    if (at == null || at < start || at > now) continue;
    const index = Math.min(
      buckets.length - 1,
      Math.floor((at - start) / (bucketMinutes * 60_000)),
    );
    buckets[index] += 1;
  }
  return buckets;
}

/** "Saved 318 MB · removed 4 audio, 6 subtitles", in the words each outcome deserves. */
export function finishedLine(item: FinishedFile): string {
  if (item.sentence) return item.sentence;
  switch (item.kind) {
    case "already":
      return "Already right · handed back as it was";
    case "passed":
      return "Passed through untouched · Weir could not process it";
    case "failed":
      return "Could not be finished · the original is untouched";
    default: {
      const removed: string[] = [];
      if (item.removedAudio) removed.push(`${item.removedAudio} audio`);
      if (item.removedSubtitles)
        removed.push(
          `${item.removedSubtitles} ${item.removedSubtitles === 1 ? "subtitle" : "subtitles"}`,
        );
      const saved = item.savedBytes
        ? `Saved ${formatBytes(item.savedBytes)}`
        : "Cleaned";
      return removed.length
        ? `${saved} · removed ${removed.join(", ")}`
        : saved;
    }
  }
}

/** "just now", "4 min ago", "2 h ago". */
export function ago(iso: string, now: number): string {
  const at = parseTime(iso);
  if (at == null) return "";
  const seconds = Math.max(0, (now - at) / 1000);
  if (seconds < 45) return "just now";
  if (seconds < 90 * 60)
    return `${Math.max(1, Math.round(seconds / 60))} min ago`;
  return `${Math.round(seconds / 3600)} h ago`;
}

/** "0:41 left", "12 min left". */
export function timeLeft(seconds: number | null): string {
  if (seconds == null || !Number.isFinite(seconds) || seconds < 0) return "";
  if (seconds < 90) return `${Math.round(seconds)} s left`;
  const minutes = Math.round(seconds / 60);
  return minutes < 90
    ? `${minutes} min left`
    : `${Math.round(minutes / 60)} h left`;
}
