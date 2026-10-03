import { describe, expect, it } from "vitest";

import { readoutFor } from "./live-trace-readout";

const clock = (time: number) => `t${time / 1000}`;
const percent = (value: number) => `${Math.round(value)}%`;
const window = { from: 0, to: 4000 };

const line = (...values: number[]) =>
  values.map((value, index) => ({ at: index * 1000, value }));

describe("the pointer's readout", () => {
  it("names the time of the nearest sample and its value", () => {
    const readout = readoutFor(
      [{ samples: line(10, 20, 30, 40, 50) }],
      window,
      0.5,
      percent,
      clock,
    );
    expect(readout).toEqual({ text: "t2 · 30%", fraction: 0.5 });
  });

  it("moves to the sample nearest, not the exact place", () => {
    const readout = readoutFor(
      [{ samples: line(10, 20, 30, 40, 50) }],
      window,
      0.4,
      percent,
      clock,
    );
    expect(readout?.text).toBe("t2 · 30%");
  });

  it("names each line when there is more than one", () => {
    const readout = readoutFor(
      [
        { label: "read", samples: line(1, 2, 3, 4, 5) },
        { label: "write", samples: line(10, 20, 30, 40, 50) },
      ],
      window,
      1,
      (value) => `${value} MB/s`,
      clock,
    );
    expect(readout?.text).toBe("t4 · read 5 MB/s · write 50 MB/s");
  });

  it("leaves out a line that has no reading at that time", () => {
    const readout = readoutFor(
      [
        { label: "read", samples: [] },
        { label: "write", samples: line(10, 20, 30, 40, 50) },
      ],
      window,
      0,
      percent,
      clock,
    );
    expect(readout?.text).toBe("t0 · write 10%");
  });

  it("is nothing when there are no samples", () => {
    expect(
      readoutFor([{ samples: [] }], window, 0.5, percent, clock),
    ).toBeNull();
  });
});
