import { useElementSize } from "../../../lib/ui/use-element-size";
import type { HandedBackBucket } from "../handed-back-model";
import { bucketReadout } from "./handed-back-words";
import { readoutLeft } from "./today-chart-model";

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
  const [readoutRef, readout] = useElementSize<HTMLDivElement>();
  const lineX = fraction * chartWidth;
  return (
    <>
      <i className="cs-cursor" style={{ left: `${lineX}px` }} />
      <div
        ref={readoutRef}
        className="cs-read"
        style={{ left: `${readoutLeft(lineX, readout.width, chartWidth)}px` }}
      >
        {bucketReadout(bucket, clock)}
      </div>
    </>
  );
}
