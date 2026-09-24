/** The last two hours of finished files, five minutes to a bucket, for the Processing toolbar. */
import type { FinishedFile } from "../../lib/activity/processing-outcome";
import { parseAppTime } from "../../lib/ui/mm-format-date";

/** How a handed-back file turned out, in the three tones Just finished uses for its dots. */
export type HandedBackTone = "ok" | "same" | "warn";

export function handedBackTone(item: FinishedFile): HandedBackTone {
  if (item.kind === "passed" || item.kind === "failed") return "warn";
  return item.kind === "already" ? "same" : "ok";
}

export type HandedBackBucket = Record<HandedBackTone, number> & {
  /** Start of the five minutes, in ms since the epoch. */
  from: number;
  total: number;
};

export type HandedBack = {
  /** Oldest first; the last one is the five minutes happening now. */
  buckets: HandedBackBucket[];
  totals: Record<HandedBackTone, number> & { all: number };
  /** The fullest five minutes, never below 1 so a scale can be drawn from it. */
  peak: number;
};

export const HANDED_BACK_BUCKET_MS = 5 * 60_000;
export const HANDED_BACK_BUCKETS = 24;

/**
 * The first instant the chart covers: 24 five-minute buckets on clock boundaries, the last one being
 * the five minutes happening now. Stable for five minutes, so a query keyed on it is not refetched
 * every second.
 */
export function handedBackSince(now: number): number {
  const current =
    Math.floor(now / HANDED_BACK_BUCKET_MS) * HANDED_BACK_BUCKET_MS;
  return current - (HANDED_BACK_BUCKETS - 1) * HANDED_BACK_BUCKET_MS;
}

/** Files handed back per five minutes over the last two hours, split by how each turned out. */
export function handedBack(finished: FinishedFile[], now: number): HandedBack {
  const since = handedBackSince(now);
  const buckets: HandedBackBucket[] = Array.from(
    { length: HANDED_BACK_BUCKETS },
    (_, index) => ({
      from: since + index * HANDED_BACK_BUCKET_MS,
      ok: 0,
      same: 0,
      warn: 0,
      total: 0,
    }),
  );
  const totals = { ok: 0, same: 0, warn: 0, all: 0 };
  for (const item of finished) {
    const at = parseAppTime(item.finishedAt);
    if (at == null || at < since || at > now) continue;
    const bucket =
      buckets[
        Math.min(
          buckets.length - 1,
          Math.floor((at - since) / HANDED_BACK_BUCKET_MS),
        )
      ];
    const tone = handedBackTone(item);
    bucket[tone] += 1;
    bucket.total += 1;
    totals[tone] += 1;
    totals.all += 1;
  }
  return {
    buckets,
    totals,
    peak: Math.max(1, ...buckets.map((bucket) => bucket.total)),
  };
}
