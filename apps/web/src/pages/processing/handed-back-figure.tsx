import { useState } from "react";
import { Link } from "react-router-dom";

import {
  LIBRARY_FILE_CLEANED_EVENT,
  REMUX_PASS_COMPLETED_EVENT,
} from "../../lib/activity/event-types";
import {
  finishedFileFromEvent,
  type FinishedFile,
} from "../../lib/activity/processing-outcome";
import { useActivityWindowQuery } from "../../lib/activity/queries";
import { plural } from "../../lib/ui/mm-plural";
import {
  handedBack,
  handedBackSince,
  HANDED_BACK_BUCKET_MS,
  type HandedBack,
  type HandedBackBucket,
  type HandedBackTone,
} from "./handed-back-model";
import type { WorkSource } from "./processing-model";

/** Five pages of 100 per kind: far more than two hours of work on any install seen so far. */
const HANDED_BACK_PAGES = 5;
const MINUTE_MS = 60_000;
const NOTHING_HANDED_BACK = "Nothing handed back in the last 2 hours.";

const TONE_WORDS: Record<HandedBackTone, string> = {
  ok: "cleaned",
  same: "already right",
  warn: "need a look",
};
const TONES = ["ok", "same", "warn"] as const;
/** Bars stack with the files that need a look on top, where they catch the eye. */
const STACK_ORDER = ["warn", "same", "ok"] as const;

/** When a bar's five minutes were, in words: "In the last 5 min", "35–40 min ago". */
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
function toneCounts(counts: Record<HandedBackTone, number>): string {
  return TONES.filter((tone) => counts[tone] > 0)
    .map((tone) => `${counts[tone].toLocaleString()} ${TONE_WORDS[tone]}`)
    .join(", ");
}

type HandedBackSummary = {
  handed: HandedBack;
  /** The server's count, which can reach further back than the bars. */
  total: number;
  /** More than a page of either kind, so the oldest files are counted but not drawn. */
  partial: boolean;
};

/**
 * The last two hours of finished files for the chosen source. Both kinds are always fetched, so
 * switching the filter only changes which ones are counted.
 */
function useHandedBack(
  filter: "all" | WorkSource,
  now: number,
): HandedBackSummary {
  const since = new Date(handedBackSince(now))
    .toISOString()
    .replace("Z", "+00:00");
  const passes = useActivityWindowQuery(
    { event_type: REMUX_PASS_COMPLETED_EVENT, date_from: since },
    HANDED_BACK_PAGES,
  );
  const cleans = useActivityWindowQuery(
    { event_type: LIBRARY_FILE_CLEANED_EVENT, date_from: since },
    HANDED_BACK_PAGES,
  );
  const responses = [
    ...(filter === "library" ? [] : [passes.data]),
    ...(filter === "download" ? [] : [cleans.data]),
  ];
  const handed = handedBack(
    responses
      .flatMap((response) => response?.items ?? [])
      .map(finishedFileFromEvent)
      .filter((item): item is FinishedFile => item !== null),
    now,
  );
  const partial = responses.some(
    (response) => response != null && !response.complete,
  );
  const total = partial
    ? responses.reduce((sum, response) => sum + (response?.total ?? 0), 0)
    : handed.totals.all;
  return { handed, total, partial };
}

/** Arrow keys walk the bars; Home and End jump to the oldest and newest. */
function nextPointed(
  key: string,
  pointed: number,
  last: number,
): number | null {
  switch (key) {
    case "ArrowLeft":
      return Math.max(0, pointed - 1);
    case "ArrowRight":
      return Math.min(last, pointed + 1);
    case "Home":
      return 0;
    case "End":
      return last;
    default:
      return null;
  }
}

/** One bar in words: "35–40 min ago · 3 cleaned". */
function bucketWords(bucket: HandedBackBucket, now: number): string {
  const counts = bucket.total === 0 ? "nothing" : toneCounts(bucket);
  return `${bucketWhen(bucket.from, now)} · ${counts}`;
}

/**
 * A slider over the bars, so the keyboard reaches each five minutes the pointer does and a screen
 * reader reads that bar's value text.
 */
function Sparkline({
  handed,
  pointed,
  now,
  onPoint,
}: {
  handed: HandedBack;
  pointed: number | null;
  now: number;
  onPoint: (index: number | null) => void;
}) {
  const last = handed.buckets.length - 1;
  const current = pointed ?? last;
  const onKeyDown = (event: React.KeyboardEvent) => {
    const next = nextPointed(event.key, current, last);
    if (next == null) return;
    event.preventDefault();
    onPoint(next);
  };
  return (
    <span
      className="mm-live-spark__bars"
      role="slider"
      tabIndex={0}
      aria-label="Files handed back, five minutes to a bar"
      aria-valuemin={0}
      aria-valuemax={last}
      aria-valuenow={current}
      aria-valuetext={bucketWords(handed.buckets[current], now)}
      onMouseLeave={() => onPoint(null)}
      onFocus={() => onPoint(last)}
      onBlur={() => onPoint(null)}
      onKeyDown={onKeyDown}
    >
      {handed.buckets.map((bucket, index) => (
        <span
          key={bucket.from}
          className={`mm-live-spark__slot${index === pointed ? " mm-live-spark__slot--pointed" : ""}`}
          aria-hidden="true"
          onMouseEnter={() => onPoint(index)}
        >
          <span
            className="mm-live-spark__bar"
            style={{
              height: `${handed.peak > 0 ? (bucket.total / handed.peak) * 100 : 0}%`,
            }}
          >
            {STACK_ORDER.map((tone) =>
              bucket[tone] > 0 ? (
                <span
                  key={tone}
                  className={`mm-live-spark__seg mm-live-spark__seg--${tone}`}
                  style={{ flexGrow: bucket[tone] }}
                />
              ) : null,
            )}
          </span>
        </span>
      ))}
    </span>
  );
}

/** The count for one tone, e.g. "4 need a look" — a link to History's failed files for the tone that means one. */
function LegendPart({ tone, count }: { tone: HandedBackTone; count: number }) {
  const words = `${count.toLocaleString()} ${TONE_WORDS[tone]}`;
  const dot = (
    <i className={`mm-live-dot mm-live-dot--${tone}`} aria-hidden="true" />
  );
  if (tone !== "warn") {
    return (
      <span className="mm-live-trend__part">
        {dot}
        {words}
      </span>
    );
  }
  return (
    <Link className="mm-live-trend__part" to="/history?show=failed">
      {dot}
      {words}
    </Link>
  );
}

function Legend({ handed }: { handed: HandedBack }) {
  return TONES.filter((tone) => handed.totals[tone] > 0).map((tone) => (
    <LegendPart key={tone} tone={tone} count={handed.totals[tone]} />
  ));
}

/**
 * The last two hours beside today's figures, where it is always on screen: the count, five minutes
 * to a bar split by how the files turned out, and the same split in words under it so nothing needs
 * a hover. Pointing at a bar, or reaching it with the arrow keys, puts that bar's five minutes in
 * the same line.
 */
export function HandedBackFigure({
  filter,
  now,
}: {
  filter: "all" | WorkSource;
  now: number;
}) {
  const { handed, total, partial } = useHandedBack(filter, now);
  const [pointed, setPointed] = useState<number | null>(null);
  const bucket = pointed == null ? null : handed.buckets[pointed];
  const words =
    total === 0
      ? NOTHING_HANDED_BACK
      : `${plural(total, "file", "files")} handed back in the last 2 hours: ${toneCounts(handed.totals)}${partial ? ". The oldest of them are not in the bars." : "."}`;
  return (
    <div
      className="mm-live-figure mm-live-figure--trend"
      data-testid="live-handed-back"
    >
      <span className="mm-live-figure__label">Last 2 hours</span>
      <span className="mm-live-figure__value" aria-hidden="true">
        {total.toLocaleString()}
      </span>
      {/* The chart keeps its place when there is nothing to draw, so switching filters never
          resizes the toolbar; it stays empty rather than a row of empty bars. */}
      {total > 0 ? (
        <Sparkline
          handed={handed}
          pointed={pointed}
          now={now}
          onPoint={setPointed}
        />
      ) : (
        <span className="mm-live-spark__bars" aria-hidden="true" />
      )}
      {/* The legend's dots and, when nothing is pointed at, the "need a look" link are the only parts
          worth reaching directly: the rest repeats the sr-only summary below. */}
      <p
        className="mm-live-trend__legend"
        aria-hidden={total > 0 && !bucket ? undefined : true}
      >
        {total === 0 ? (
          NOTHING_HANDED_BACK
        ) : bucket ? (
          <span className="mm-live-trend__pointed">
            {bucketWords(bucket, now)}
          </span>
        ) : (
          <Legend handed={handed} />
        )}
      </p>
      <span className="sr-only" data-testid="live-handed-back-sum">
        {words}
      </span>
    </div>
  );
}
