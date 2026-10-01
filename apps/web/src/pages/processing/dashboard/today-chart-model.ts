/** The geometry of the Today chart: a filled line over the last two hours, one point per five minutes. */
import type { HandedBackBucket } from "../handed-back-model";

/** The drawing's own units; the SVG stretches to the tile. */
export const CHART_WIDTH = 240;
export const CHART_HEIGHT = 56;
/** Room kept above the highest point and below the lowest, so the line is never clipped. */
const PAD_TOP = 4;
const PAD_BOTTOM = 2;

export type ChartPoint = { x: number; y: number };

export type ChartShape = {
  points: ChartPoint[];
  /** The line alone. */
  line: string;
  /** The line closed down to the floor, to be filled. */
  area: string;
};

/** Files finished per bucket as a line from the oldest bucket (left) to the newest (right). */
export function chartShape(
  buckets: readonly HandedBackBucket[],
  peak: number,
): ChartShape {
  const last = Math.max(1, buckets.length - 1);
  const plot = CHART_HEIGHT - PAD_TOP - PAD_BOTTOM;
  const points = buckets.map((bucket, index) => ({
    x: (index / last) * CHART_WIDTH,
    y: PAD_TOP + plot - (peak > 0 ? bucket.total / peak : 0) * plot,
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
