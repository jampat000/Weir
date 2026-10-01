/** The last two hours of finished files, in words: for the Today chart's caption and its screen-reader text. */
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

/** One bucket in words: "35–40 min ago · 3 cleaned". */
export function bucketWords(bucket: HandedBackBucket, now: number): string {
  const counts = bucket.total === 0 ? "nothing" : toneCounts(bucket);
  return `${bucketWhen(bucket.from, now)} · ${counts}`;
}

/** The line under the chart: the bucket pointed at, or the whole two hours as counts. */
export function chartCaption(
  handed: HandedBack,
  pointed: number | null,
  now: number,
): string {
  if (pointed !== null) return bucketWords(handed.buckets[pointed], now);
  if (handed.totals.all === 0) return NOTHING_HANDED_BACK;
  return `Last 2 hours · ${toneCounts(handed.totals)}`;
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
