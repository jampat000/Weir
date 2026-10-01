/** The words and numbers on the Processing cards: times, speeds and how a finished file turned out. */
import type { FinishedFile } from "../../lib/activity/processing-outcome";
import { formatBytes } from "../../lib/format/bytes";
import type { ProcessingLibrary } from "../../lib/processing/libraries-api";
import {
  workflowKindName,
  type WorkflowKind,
} from "../../lib/processing/workflow-kind";
import { parseAppTime } from "../../lib/ui/mm-format-date";
import { plural } from "../../lib/ui/mm-plural";
import type { ArrivingItem, WorkingItem } from "./processing-model";

/** The kind of each workflow that is switched on, which decides where the page says a file ends up. */
export function enabledWorkflowKinds(
  workflows: readonly Pick<
    ProcessingLibrary,
    "enabled" | "manager_connection_ids"
  >[],
): WorkflowKind["kind"][] {
  return workflows.filter((workflow) => workflow.enabled).map(workflowKindName);
}

/** Which kinds of workflow the page covers. With none, it reads as linked. */
type WorkflowMix = "linked" | "weir_only" | "mixed";

function workflowMix(
  workflowKinds: readonly WorkflowKind["kind"][],
): WorkflowMix {
  const weirOnly = workflowKinds.includes("weir_only");
  const linked = workflowKinds.includes("linked");
  if (weirOnly && linked) return "mixed";
  return weirOnly ? "weir_only" : "linked";
}

const HANDING_HINT: Record<WorkflowMix, string> = {
  linked: "Final checks, then back to your media manager",
  weir_only: "Final checks, then into the output folder",
  mixed:
    "Final checks, then into the output folder or back to your media manager",
};

const PAGE_LEAD: Record<WorkflowMix, string> = {
  linked:
    "Every file Weir is working on, from the moment it lands to the moment your media manager has it back.",
  weir_only:
    "Every file Weir is working on, from the moment it lands to the moment its cleaned copy is in the output folder.",
  mixed:
    "Every file Weir is working on, from the moment it lands to the moment its cleaned copy is in the output folder or your media manager has it back.",
};

/** Where the Handing back lane says a file goes next, in the words of the workflows that exist. */
export function handingLaneHint(
  workflowKinds: readonly WorkflowKind["kind"][],
): string {
  return HANDING_HINT[workflowMix(workflowKinds)];
}

/** The Processing page's opening line, ending where a file ends up for the workflows that exist. */
export function processingLead(
  workflowKinds: readonly WorkflowKind["kind"][],
): string {
  return PAGE_LEAD[workflowMix(workflowKinds)];
}

/** The tracks a pass took out, as words: "2 audio", "4 subtitles". */
export function removedTrackWords(tracks: {
  removedAudio: number;
  removedSubtitles: number;
}): string[] {
  const removed: string[] = [];
  if (tracks.removedAudio) removed.push(`${tracks.removedAudio} audio`);
  if (tracks.removedSubtitles) {
    removed.push(plural(tracks.removedSubtitles, "subtitle", "subtitles"));
  }
  return removed;
}

/** "Saved 318 MB · removed 4 audio, 6 subtitles", in the words each outcome deserves. */
export function finishedLine(item: FinishedFile): string {
  if (item.sentence) return item.sentence;
  switch (item.kind) {
    case "already":
      return "Already right · nothing to change";
    case "passed":
      return "Passed through untouched · Weir could not process it";
    case "failed":
      return "Could not be finished · the original is untouched";
    default: {
      const removed = removedTrackWords(item);
      const saved = item.savedBytes
        ? `Saved ${formatBytes(item.savedBytes)}`
        : "Cleaned";
      return removed.length
        ? `${saved} · removed ${removed.join(", ")}`
        : saved;
    }
  }
}

/** A moment younger than this is "just now". */
const JUST_NOW_SECONDS = 45;

/** Whether something at this time happened just now. */
export function isJustNow(iso: string, now: number): boolean {
  const at = parseAppTime(iso);
  return at != null && Math.max(0, (now - at) / 1000) < JUST_NOW_SECONDS;
}

/** "just now", "4 min ago", "2 h ago". */
export function ago(iso: string, now: number): string {
  const at = parseAppTime(iso);
  if (at == null) return "";
  const seconds = Math.max(0, (now - at) / 1000);
  if (isJustNow(iso, now)) return "just now";
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

/**
 * ffmpeg's speed ("1.26e+03x", "35.2x", "0.8x") as a person reads it: "1,260×", "35×", "0.8×". ffmpeg
 * switches to scientific notation past 999, which reached the screen as it was.
 */
export function speedWords(raw: string | null | undefined): string | null {
  const value = Number.parseFloat((raw ?? "").trim().replace(/x$/i, ""));
  if (!Number.isFinite(value) || value <= 0) return null;
  const shown =
    value >= 10
      ? Math.round(value).toLocaleString("en-US")
      : value.toFixed(1).replace(/\.0$/, "");
  return `${shown}×`;
}

/** How fast the source is being read, from how far through it the pass is: bytes a second, or null. */
export function readRate(
  sizeBytes: number | null | undefined,
  percent: number | null | undefined,
  elapsedSeconds: number | null | undefined,
): number | null {
  if (!sizeBytes || percent == null || !elapsedSeconds || elapsedSeconds < 1)
    return null;
  const rate =
    (sizeBytes * Math.min(100, Math.max(0, percent))) / 100 / elapsedSeconds;
  return rate > 0 && Number.isFinite(rate) ? rate : null;
}

/** A running length as a player shows it: "4:05", "18:40", "1:02:03". */
export function clock(seconds: number | null | undefined): string {
  if (seconds == null || !Number.isFinite(seconds) || seconds < 0) return "";
  const whole = Math.floor(seconds);
  const h = Math.floor(whole / 3600);
  const m = Math.floor((whole % 3600) / 60);
  const sec = String(whole % 60).padStart(2, "0");
  return h > 0 ? `${h}:${String(m).padStart(2, "0")}:${sec}` : `${m}:${sec}`;
}

/** How long something has been running: "41 s", "2 min 14 s", "1 h 5 min". */
export function runningFor(seconds: number | null | undefined): string {
  if (seconds == null || !Number.isFinite(seconds) || seconds < 0) return "";
  const whole = Math.floor(seconds);
  if (whole < 60) return `${whole} s`;
  if (whole < 3600) {
    const rest = whole % 60;
    return rest
      ? `${Math.floor(whole / 60)} min ${rest} s`
      : `${whole / 60} min`;
  }
  const minutes = Math.floor((whole % 3600) / 60);
  return minutes
    ? `${Math.floor(whole / 3600)} h ${minutes} min`
    : `${whole / 3600} h`;
}

/** The few characters that fit inside a countdown ring: "58s", "9m", "2h". */
export function ringLabel(seconds: number): string {
  if (seconds < 100) return `${Math.round(seconds)}s`;
  if (seconds < 100 * 60) return `${Math.round(seconds / 60)}m`;
  return `${Math.round(seconds / 3600)}h`;
}

/**
 * What an arriving file's ring can honestly say, from the seconds left to its deadline (its own hold, or Weir's next
 * look at its library). Counting: that moment is ahead. Checking: it has come, and Weir is looking now (a scan books the
 * next look for the end of every hold it sets), so the ring turns because something is happening. Unknown: no time at
 * all is known, so the ring stays still and the reason underneath says why.
 */
export function ringState(
  left: number | null,
): "counting" | "checking" | "unknown" {
  if (left == null) return "unknown";
  return left > 0 ? "counting" : "checking";
}

/** How far through its wait an arriving file is, or null when the wait has no known length. */
export function ringFraction(
  item: ArrivingItem,
  left: number | null,
): number | null {
  const state = ringState(left);
  if (state === "checking") return 1;
  const total =
    item.holdUntil != null ? item.holdTotal : item.nextLook?.interval;
  if (state !== "counting" || !total || left == null) return null;
  return Math.min(1, Math.max(0, 1 - left / total));
}

/** The numbers a running pass reports, each as the words a card shows, or null when the server has not said. */
export type WorkingFigures = {
  /** ffmpeg's speed as people read it: "148×". */
  speed: string | null;
  /** How fast the source is being read: "35 MB/s". */
  reading: string | null;
  /** How far into the file the pass is: "4:05 of 1:32:10". */
  through: string | null;
  /** How long the pass has been going: "2 min 14 s". */
  running: string | null;
};

export function workingFigures(item: WorkingItem): WorkingFigures {
  const file = item.file;
  const rate = readRate(
    file?.size_bytes,
    item.percent,
    file?.progress_elapsed_seconds,
  );
  const duration = file?.duration_seconds ?? null;
  const elapsed = file?.progress_elapsed_seconds ?? 0;
  return {
    speed: speedWords(item.speed),
    reading: rate ? `${formatBytes(rate)}/s` : null,
    through:
      duration && item.percent != null
        ? `${clock((duration * Math.min(100, item.percent)) / 100)} of ${clock(duration)}`
        : null,
    running: elapsed >= 1 ? runningFor(elapsed) : null,
  };
}
