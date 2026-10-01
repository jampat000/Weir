/** The last two hours of finished files, in words: for the Today chart's labels, readout and screen-reader text. */
import { plural } from "../../../lib/ui/mm-plural";
import {
  HANDED_BACK_BUCKET_MS,
  type HandedBack,
  type HandedBackBucket,
  type HandedBackTone,
} from "../handed-back-model";

const MINUTE_MS = 60_000;

export const NOTHING_HANDED_BACK = "Nothing finished in the last 2 hours.";

const TONE_WORDS: Record<HandedBackTone, string> = {
  ok: "cleaned",
  same: "already right",
  warn: "need a look",
};
const TONES = ["ok", "same", "warn"] as const;

/** When a bucket's five minutes were, in words: "In the last 5 min", "35–40 min ago". */
function bucketWhen(from: number, now: number): string {
  const newest = Math.max(
    0,
    Math.round((now - from - HANDED_BACK_BUCKET_MS) / MINUTE_MS),
  );
  const oldest = Math.round((now - from) / MINUTE_MS);
  return newest === 0
    ? `In the last ${oldest} min`
    : `${newest}–${oldest} min ago`;
}

/** "3 cleaned, 1 already right" for a bucket or the whole two hours. */
export function toneCounts(counts: Record<HandedBackTone, number>): string {
  return TONES.filter((tone) => counts[tone] > 0)
    .map((tone) => `${counts[tone].toLocaleString()} ${TONE_WORDS[tone]}`)
    .join(", ");
}

/** What finished in one bucket: "3 cleaned, 1 already right", or "nothing". */
function bucketCounts(bucket: HandedBackBucket): string {
  return bucket.total === 0 ? "nothing" : toneCounts(bucket);
}

/** One bucket in words: "35–40 min ago · 3 cleaned". */
export function bucketWords(bucket: HandedBackBucket, now: number): string {
  return `${bucketWhen(bucket.from, now)} · ${bucketCounts(bucket)}`;
}

/** A gridline's value with the unit the chart plots: "10 files", "1 file". */
export function scaleLabel(value: number): string {
  return plural(value, "file", "files");
}

const HOUR_MS = 60 * MINUTE_MS;

/** The chart's time span in one label, from how far back the oldest point is: "2 h ago – now". */
export function spanLabel(handed: HandedBack, now: number): string {
  const oldest = handed.buckets[0];
  const hours = oldest
    ? Math.max(1, Math.round((now - oldest.from) / HOUR_MS))
    : 2;
  return `${hours} h ago – now`;
}

/** The pointer's readout for one bucket: its clock time and what finished then, "2:10 pm · 3 cleaned". */
export function bucketReadout(
  bucket: HandedBackBucket,
  clock: (ms: number) => string,
): string {
  return `${clock(bucket.from)} · ${bucketCounts(bucket)}`;
}

/** The whole two hours in one sentence, for a screen reader. */
export function handedBackSentence(
  handed: HandedBack,
  total: number,
  partial: boolean,
): string {
  if (total === 0) return NOTHING_HANDED_BACK;
  const tail = partial ? ". The oldest of them are not in the chart." : ".";
  return `${plural(total, "file", "files")} finished in the last 2 hours: ${toneCounts(handed.totals)}${tail}`;
}
