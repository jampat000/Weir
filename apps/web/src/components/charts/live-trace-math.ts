/**
 * The maths of a live trace: a line over a window of time whose head glides from the second-newest sample to the
 * newest while the next one is awaited, so the whole line slides left continuously between one-second samples.
 * Everything here is a plain function of the samples and a time; the component only draws what these say.
 */
import { roundScaleTop } from "../../lib/ui/chart-scale";

/** One reading: when it was taken, in ms since the epoch, and its value. Samples are oldest first. */
export type TraceSample = { at: number; value: number };

/** The drawing's own units: the SVG stretches to the card. */
export const TRACE_WIDTH = 200;
export const TRACE_HEIGHT = 50;
/** Room kept above the top of the scale and below the floor, so the line's stroke is never clipped. */
const PAD_TOP = 3;
const PAD_BOTTOM = 2;
const PLOT_HEIGHT = TRACE_HEIGHT - PAD_TOP - PAD_BOTTOM;
/** A newest sample later than this after its predecessor is a catch-up, not a second's glide: the head is there at once. */
const MAX_GLIDE_MS = 3000;

export type TraceWindow = { from: number; to: number };

export type TracePoint = { x: number; y: number };

export type TraceShape = {
  points: TracePoint[];
  /** The line alone. */
  line: string;
  /** The line closed down to the floor, to be filled. */
  area: string;
};

const clamp = (value: number, min: number, max: number) =>
  Math.min(max, Math.max(min, value));

/**
 * The time at the head of the line, `sinceLatest` ms after the newest sample reached the screen: it glides from the
 * second-newest sample's time to the newest's over the gap between them, then stays. Null with nothing to draw.
 */
export function headTime(
  samples: readonly TraceSample[],
  sinceLatest: number,
): number | null {
  const latest = samples.at(-1);
  if (!latest) return null;
  const previous = samples.at(-2);
  if (!previous) return latest.at;
  const gap = latest.at - previous.at;
  if (gap <= 0 || gap > MAX_GLIDE_MS) return latest.at;
  return previous.at + gap * clamp(sinceLatest / gap, 0, 1);
}

/** The window of `windowMs` that ends at `head`. */
export function windowEndingAt(head: number, windowMs: number): TraceWindow {
  return { from: head - windowMs, to: head };
}

/** The value of the line at `time`: straight between the samples either side of it; null outside the samples. */
export function valueAt(
  samples: readonly TraceSample[],
  time: number,
): number | null {
  const first = samples[0];
  const last = samples.at(-1);
  if (!first || !last || time < first.at || time > last.at) return null;
  const after = samples.findIndex((sample) => sample.at >= time);
  const next = samples[after];
  const before = samples[after - 1];
  if (!before || next.at === time) return next.value;
  const share = (time - before.at) / (next.at - before.at);
  return before.value + (next.value - before.value) * share;
}

/**
 * The samples inside the window, with the line's value at the window's two edges added where a sample lies beyond
 * them, so the line meets both edges instead of stopping short of them.
 */
export function samplesInWindow(
  samples: readonly TraceSample[],
  { from, to }: TraceWindow,
): TraceSample[] {
  const inside = samples.filter(
    (sample) => sample.at >= from && sample.at <= to,
  );
  const edges: TraceSample[] = [];
  const atFrom = valueAt(samples, from);
  if (atFrom !== null && inside[0]?.at !== from)
    edges.push({ at: from, value: atFrom });
  const atTo = valueAt(samples, to);
  const tail =
    atTo !== null && inside.at(-1)?.at !== to ? [{ at: to, value: atTo }] : [];
  return [...edges, ...inside, ...tail];
}

/** The highest value on the line inside the window; 0 when there is none. */
export function highestIn(
  samples: readonly TraceSample[],
  window: TraceWindow,
): number {
  return samplesInWindow(samples, window).reduce(
    (highest, sample) => Math.max(highest, sample.value),
    0,
  );
}

/**
 * The top of a rate's scale: a round number at or above the highest value shown, and never under `floorTop`, so a
 * quiet trace shows as quiet instead of being stretched to fill the card.
 */
export function rateScaleTop(highest: number, floorTop: number): number {
  return Math.max(floorTop, roundScaleTop(highest));
}

function format(value: number): string {
  return value.toFixed(2);
}

/** The line, and its filled area, for the samples in the window on a scale from 0 to `top`, in the drawing's units. */
export function traceShape(
  samples: readonly TraceSample[],
  window: TraceWindow,
  top: number,
): TraceShape {
  const span = window.to - window.from;
  const points = samplesInWindow(samples, window).map((sample) => ({
    x:
      span > 0 ? ((sample.at - window.from) / span) * TRACE_WIDTH : TRACE_WIDTH,
    y: PAD_TOP + PLOT_HEIGHT * (1 - clamp(sample.value / top, 0, 1)),
  }));
  if (points.length === 0) return { points, line: "", area: "" };
  const line = points
    .map(
      (point, index) =>
        `${index === 0 ? "M" : "L"}${format(point.x)} ${format(point.y)}`,
    )
    .join(" ");
  const floor = TRACE_HEIGHT - PAD_BOTTOM;
  const first = points[0];
  const last = points[points.length - 1];
  const area = `${line} L${format(last.x)} ${floor} L${format(first.x)} ${floor} Z`;
  return { points, line, area };
}

/** The sample whose time is nearest `time`, or null with no samples. */
export function nearestSample(
  samples: readonly TraceSample[],
  time: number,
): TraceSample | null {
  let nearest: TraceSample | null = null;
  for (const sample of samples) {
    if (
      nearest === null ||
      Math.abs(sample.at - time) < Math.abs(nearest.at - time)
    )
      nearest = sample;
  }
  return nearest;
}

/** The time under a pointer `fraction` of the way across the window (0 at its left edge, 1 at its right). */
export function timeAtFraction(window: TraceWindow, fraction: number): number {
  return window.from + clamp(fraction, 0, 1) * (window.to - window.from);
}

/** How far across the window, 0 to 1, a time falls. */
export function fractionOfTime(window: TraceWindow, time: number): number {
  const span = window.to - window.from;
  return span > 0 ? clamp((time - window.from) / span, 0, 1) : 1;
}

/** A readout's words: the clock time, then each reading's own words, "2:14:05 pm · read 4 MB/s · write 12 MB/s". */
export function readoutText(
  clock: string,
  readings: readonly string[],
): string {
  return [clock, ...readings].join(" · ");
}
