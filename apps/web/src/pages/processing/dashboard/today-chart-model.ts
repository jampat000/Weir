/** The geometry of the Today chart: a filled line over the last two hours, one point per five minutes. */
import type { HandedBackBucket } from "../handed-back-model";

/** The drawing's own units; the SVG stretches to the tile. */
export const CHART_WIDTH = 240;
export const CHART_HEIGHT = 56;
/**
 * Room kept above the highest point, so the line is never clipped, and below the lowest, where the labels
 * of the time span sit inside the chart's bottom edge.
 */
const PAD_TOP = 4;
const PAD_BOTTOM = 9;

export type ChartPoint = { x: number; y: number };

export type ChartShape = {
  points: ChartPoint[];
  /** The line alone. */
  line: string;
  /** The line closed down to the floor, to be filled. */
  area: string;
};

const PLOT_HEIGHT = CHART_HEIGHT - PAD_TOP - PAD_BOTTOM;

/** Where a value sits on a scale that runs from 0 at the floor to `scaleTop` at the top, in drawing units. */
function valueY(value: number, scaleTop: number): number {
  return PAD_TOP + PLOT_HEIGHT - (value / scaleTop) * PLOT_HEIGHT;
}

export type ScaleLine = { value: number; y: number };

/** The gridlines: the top of the scale and half of it. */
export function scaleLines(scaleTop: number): ScaleLine[] {
  return [scaleTop, scaleTop / 2].map((value) => ({
    value,
    y: valueY(value, scaleTop),
  }));
}

/** Files finished per bucket as a line from the oldest bucket (left) to the newest (right). */
export function chartShape(
  buckets: readonly HandedBackBucket[],
  scaleTop: number,
): ChartShape {
  const last = Math.max(1, buckets.length - 1);
  const points = buckets.map((bucket, index) => ({
    x: (index / last) * CHART_WIDTH,
    y: valueY(bucket.total, scaleTop),
  }));
  const line = points
    .map(
      (point, index) =>
        `${index === 0 ? "M" : "L"}${point.x.toFixed(1)} ${point.y.toFixed(1)}`,
    )
    .join(" ");
  const floor = CHART_HEIGHT - PAD_BOTTOM;
  const area =
    points.length === 0 ? "" : `${line} L${CHART_WIDTH} ${floor} L0 ${floor} Z`;
  return { points, line, area };
}

/** The bucket nearest a place across the chart: 0 is the left edge, 1 the right edge. */
export function pointedAtFraction(fraction: number, last: number): number {
  return Math.min(last, Math.max(0, Math.round(fraction * last)));
}

/** How far the readout sits from the pointer's line, and the least it keeps from the chart's edges. */
const READOUT_OFFSET = 10;
const READOUT_INSET = 6;

/**
 * Where the readout's left edge goes: just right of the pointer's line, or just left of it when it would
 * run past the chart's right edge, and never closer than the inset to either edge.
 */
export function readoutLeft(
  lineX: number,
  readoutWidth: number,
  chartWidth: number,
): number {
  const right = lineX + READOUT_OFFSET;
  const fits = right + readoutWidth <= chartWidth - READOUT_INSET;
  const wanted = fits ? right : lineX - READOUT_OFFSET - readoutWidth;
  const furthest = chartWidth - readoutWidth - READOUT_INSET;
  return Math.max(READOUT_INSET, Math.min(wanted, furthest));
}

/** The bucket the arrow keys move to: left and right walk, Home and End jump to the oldest and newest. */
export function pointedAfterKey(
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
