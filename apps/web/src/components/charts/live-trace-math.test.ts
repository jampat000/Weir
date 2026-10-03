import { describe, expect, it } from "vitest";

import {
  TRACE_HEIGHT,
  TRACE_WIDTH,
  fractionOfTime,
  headTime,
  highestIn,
  nearestSample,
  rateScaleTop,
  readoutText,
  samplesInWindow,
  timeAtFraction,
  traceShape,
  valueAt,
  windowEndingAt,
  type TraceSample,
} from "./live-trace-math";

const samples = (...values: number[]): TraceSample[] =>
  values.map((value, index) => ({ at: index * 1000, value }));

describe("the head of the line", () => {
  it("is at the second-newest sample the moment the newest arrives", () => {
    expect(headTime(samples(1, 2, 3), 0)).toBe(1000);
  });

  it("glides to the newest sample over the gap between them", () => {
    expect(headTime(samples(1, 2, 3), 500)).toBe(1500);
    expect(headTime(samples(1, 2, 3), 1000)).toBe(2000);
  });

  it("waits at the newest sample once the next one is late", () => {
    expect(headTime(samples(1, 2, 3), 5000)).toBe(2000);
  });

  it("is at the only sample when there is one, and nowhere when there is none", () => {
    expect(headTime(samples(7), 0)).toBe(0);
    expect(headTime([], 0)).toBeNull();
  });

  it("is at the newest sample at once after a long gap, which is a catch-up", () => {
    const caughtUp: TraceSample[] = [
      { at: 0, value: 1 },
      { at: 60_000, value: 2 },
    ];
    expect(headTime(caughtUp, 0)).toBe(60_000);
  });
});

describe("a window of time", () => {
  it("ends at the head and reaches back by its length", () => {
    expect(windowEndingAt(10_000, 4000)).toEqual({ from: 6000, to: 10_000 });
  });

  it("maps a pointer's place across it to a time and back", () => {
    const window = { from: 1000, to: 5000 };
    expect(timeAtFraction(window, 0.25)).toBe(2000);
    expect(fractionOfTime(window, 2000)).toBe(0.25);
  });

  it("keeps a pointer beyond its edges on the edge", () => {
    const window = { from: 1000, to: 5000 };
    expect(timeAtFraction(window, 2)).toBe(5000);
    expect(fractionOfTime(window, 0)).toBe(0);
  });
});

describe("the value of the line at a time", () => {
  it("is straight between two samples", () => {
    expect(valueAt(samples(0, 10), 250)).toBe(2.5);
  });

  it("is a sample's own value at its time", () => {
    expect(valueAt(samples(3, 9), 1000)).toBe(9);
    expect(valueAt(samples(3, 9), 0)).toBe(3);
  });

  it("is nothing outside the samples", () => {
    expect(valueAt(samples(3, 9), -1)).toBeNull();
    expect(valueAt(samples(3, 9), 1001)).toBeNull();
    expect(valueAt([], 0)).toBeNull();
  });
});

describe("the samples in a window", () => {
  it("adds the line's value at each edge, so the line meets both", () => {
    expect(
      samplesInWindow(samples(0, 10, 20, 30), { from: 500, to: 2500 }),
    ).toEqual([
      { at: 500, value: 5 },
      { at: 1000, value: 10 },
      { at: 2000, value: 20 },
      { at: 2500, value: 25 },
    ]);
  });

  it("adds nothing at an edge that falls on a sample", () => {
    expect(
      samplesInWindow(samples(0, 10, 20), { from: 1000, to: 2000 }),
    ).toEqual([
      { at: 1000, value: 10 },
      { at: 2000, value: 20 },
    ]);
  });

  it("starts where the samples start when the window reaches back further", () => {
    const inside = samplesInWindow(samples(5, 6), { from: -5000, to: 1000 });
    expect(inside[0]).toEqual({ at: 0, value: 5 });
  });

  it("finds the highest value inside the window, edges included", () => {
    expect(highestIn(samples(50, 0, 0, 0, 8), { from: 500, to: 3000 })).toBe(
      25,
    );
  });
});

describe("the shape of a trace", () => {
  const window = { from: 0, to: 2000 };

  it("puts the oldest sample at the left edge and the newest at the right", () => {
    const shape = traceShape(samples(0, 5, 10), window, 10);
    expect(shape.points[0].x).toBe(0);
    expect(shape.points.at(-1)?.x).toBe(TRACE_WIDTH);
  });

  it("puts a value of zero at the floor and the top of the scale near the ceiling", () => {
    const shape = traceShape(samples(0, 10), { from: 0, to: 1000 }, 10);
    expect(shape.points[0].y).toBeGreaterThan(shape.points[1].y);
    expect(shape.points[0].y).toBeLessThan(TRACE_HEIGHT);
    expect(shape.points[1].y).toBeGreaterThan(0);
  });

  it("holds a value above the top of the scale at the ceiling", () => {
    const shape = traceShape(samples(100, 10), { from: 0, to: 1000 }, 10);
    expect(shape.points[0].y).toBe(shape.points[1].y);
  });

  it("writes the line as a path and closes the area down to the floor", () => {
    const shape = traceShape(samples(0, 10), { from: 0, to: 1000 }, 10);
    expect(shape.line.startsWith("M0.00 ")).toBe(true);
    expect(shape.line).toContain(" L200.00 ");
    expect(shape.area.endsWith("Z")).toBe(true);
  });

  it("is empty with no samples in the window", () => {
    expect(traceShape([], window, 10)).toEqual({
      points: [],
      line: "",
      area: "",
    });
  });

  it("slides left as the window moves on", () => {
    const peakX = (shape: ReturnType<typeof traceShape>) =>
      shape.points.reduce((best, point) => (point.y < best.y ? point : best)).x;
    const line = samples(0, 5, 10, 5);
    const first = traceShape(line, { from: 0, to: 3000 }, 10);
    const later = traceShape(line, { from: 500, to: 3500 }, 10);
    expect(peakX(first)).toBeCloseTo(133.33, 1);
    expect(peakX(later)).toBeCloseTo(100, 1);
  });
});

describe("the scale of a rate", () => {
  it("is a round number at or above the highest value", () => {
    expect(rateScaleTop(3.2, 1)).toBe(5);
    expect(rateScaleTop(40, 1)).toBe(50);
  });

  it("is never under the floor, so a quiet trace stays low", () => {
    expect(rateScaleTop(0.03, 1)).toBe(1);
    expect(rateScaleTop(0, 10)).toBe(10);
  });
});

describe("the sample under the pointer", () => {
  it("is the one nearest the time", () => {
    expect(nearestSample(samples(1, 2, 3), 1400)?.value).toBe(2);
    expect(nearestSample(samples(1, 2, 3), 1600)?.value).toBe(3);
  });

  it("is nothing when there are no samples", () => {
    expect(nearestSample([], 0)).toBeNull();
  });
});

describe("the readout's words", () => {
  it("puts the clock time first and the readings after it", () => {
    expect(readoutText("2:14:05 pm", ["37%"])).toBe("2:14:05 pm · 37%");
    expect(readoutText("2:14:05 pm", ["read 4.0 MB/s", "write 12 MB/s"])).toBe(
      "2:14:05 pm · read 4.0 MB/s · write 12 MB/s",
    );
  });
});
