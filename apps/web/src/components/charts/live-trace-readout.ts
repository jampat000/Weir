import {
  fractionOfTime,
  nearestSample,
  readoutText,
  timeAtFraction,
  valueAt,
  type TraceSample,
  type TraceWindow,
} from "./live-trace-math";

/** One line of a trace, as the readout needs it. */
export type ReadoutSeries = {
  /** Named in the readout when the trace has more than one line: "read", "write". */
  label?: string;
  samples: readonly TraceSample[];
};

/** What the pointer's card says, and how far across the trace (0 to 1) the sample it describes lies. */
export type Readout = { text: string; fraction: number };

/**
 * The readout for a pointer `fraction` of the way across the window: the clock time of the nearest sample of the
 * first line that has any, then what every line reads at that time. Null when the trace has no samples.
 */
export function readoutFor(
  series: readonly ReadoutSeries[],
  window: TraceWindow,
  fraction: number,
  format: (value: number) => string,
  clock: (time: number) => string,
): Readout | null {
  const lead = series.find((line) => line.samples.length > 0);
  const near = lead
    ? nearestSample(lead.samples, timeAtFraction(window, fraction))
    : null;
  if (!near) return null;
  const readings = series.flatMap((line) => {
    const value = valueAt(line.samples, near.at);
    if (value === null) return [];
    const text = format(value);
    return [series.length > 1 && line.label ? `${line.label} ${text}` : text];
  });
  return {
    text: readoutText(clock(near.at), readings),
    fraction: fractionOfTime(window, near.at),
  };
}
