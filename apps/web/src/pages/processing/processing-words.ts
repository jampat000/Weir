/** The words and numbers on the Processing cards: times, speeds and how a finished file turned out. */
import type { FinishedFile } from "../../lib/activity/processing-outcome";
import { formatBytes } from "../../lib/format/bytes";
import { parseAppTime } from "../../lib/ui/mm-format-date";
import { plural } from "../../lib/ui/mm-plural";

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
      const removed: string[] = [];
      if (item.removedAudio) removed.push(`${item.removedAudio} audio`);
      if (item.removedSubtitles) {
        removed.push(plural(item.removedSubtitles, "subtitle", "subtitles"));
      }
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
  const at = parseAppTime(iso);
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
