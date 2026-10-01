import type { KeyboardEvent } from "react";

import type { HandedBack } from "../handed-back-model";
import { bucketWords } from "./handed-back-words";
import {
  CHART_HEIGHT,
  CHART_WIDTH,
  chartShape,
  pointedAfterKey,
} from "./today-chart-model";

const GRADIENT_ID = "mm-today-fill";

type TodayChartProps = {
  handed: HandedBack;
  /** The bucket the pointer or keyboard is on, or null for the newest. */
  pointed: number | null;
  now: number;
  onPoint: (index: number | null) => void;
};

/**
 * The last two hours as a filled line, five minutes to a point. It is a slider over the points, so the
 * keyboard reaches each five minutes the pointer does, and a screen reader reads that point's value text.
 */
export function TodayChart({ handed, pointed, now, onPoint }: TodayChartProps) {
  const last = handed.buckets.length - 1;
  const current = pointed ?? last;
  const shape = chartShape(handed.buckets, handed.peak);
  const dot = shape.points[current];
  const onKeyDown = (event: KeyboardEvent) => {
    const next = pointedAfterKey(event.key, current, last);
    if (next === null) return;
    event.preventDefault();
    onPoint(next);
  };
  return (
    <span
      className="mm-today-chart"
      role="slider"
      tabIndex={0}
      aria-label="Files finished, five minutes to a point"
      aria-valuemin={0}
      aria-valuemax={last}
      aria-valuenow={current}
      aria-valuetext={bucketWords(handed.buckets[current], now)}
      onMouseLeave={() => onPoint(null)}
      onFocus={() => onPoint(last)}
      onBlur={() => onPoint(null)}
      onKeyDown={onKeyDown}
    >
      <svg
        aria-hidden="true"
        viewBox={`0 0 ${CHART_WIDTH} ${CHART_HEIGHT}`}
        preserveAspectRatio="none"
        className="mm-today-chart__svg"
      >
        <defs>
          <linearGradient id={GRADIENT_ID} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0" className="mm-today-chart__stop" />
            <stop
              offset="1"
              className="mm-today-chart__stop mm-today-chart__stop--end"
            />
          </linearGradient>
        </defs>
        <path d={shape.area} fill={`url(#${GRADIENT_ID})`} />
        <path
          d={shape.line}
          className="mm-today-chart__line"
          fill="none"
          vectorEffect="non-scaling-stroke"
        />
      </svg>
      {dot ? (
        <span
          aria-hidden="true"
          className="mm-today-chart__dot"
          style={{
            left: `${(dot.x / CHART_WIDTH) * 100}%`,
            top: `${(dot.y / CHART_HEIGHT) * 100}%`,
          }}
        />
      ) : null}
      <span className="mm-today-chart__slots" aria-hidden="true">
        {handed.buckets.map((bucket, index) => (
          <span key={bucket.from} onMouseEnter={() => onPoint(index)} />
        ))}
      </span>
    </span>
  );
}
