import { bucketReadout } from "./handed-back-words";
import { READOUT_GAP, readoutSide } from "./today-chart-model";
import type { HandedBackBucket } from "../handed-back-model";
import { useElementSize } from "../../../lib/ui/use-element-size";

type TodayChartReadoutProps = {
  bucket: HandedBackBucket;
  /** Where the pointer's line is, across the chart: 0 at the left edge, 1 at the right. */
  fraction: number;
  /** The chart's width in pixels, so the readout can keep inside it. */
  chartWidth: number;
  /** Writes a time as a clock time in Weir's time zone. */
  clock: (ms: number) => string;
};

/** The pointer's line and the small card beside it that says what finished at that time. */
export function TodayChartReadout({
  bucket,
  fraction,
  chartWidth,
  clock,
}: TodayChartReadoutProps) {
  const [readoutRef, readout] = useElementSize<HTMLSpanElement>();
  const side = readoutSide(fraction * chartWidth, readout.width, chartWidth);
  const left = `${fraction * 100}%`;
  return (
    <span aria-hidden="true">
      <span className="mm-today-chart__pointer" style={{ left }} />
      <span
        ref={readoutRef}
        className={`mm-today-chart__readout mm-today-chart__readout--${side}`}
        style={{ left, ["--today-readout-gap" as string]: `${READOUT_GAP}px` }}
      >
        {bucketReadout(bucket, clock)}
      </span>
    </span>
  );
}
