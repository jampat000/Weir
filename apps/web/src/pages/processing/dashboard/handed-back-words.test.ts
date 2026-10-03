import { describe, expect, it } from "vitest";

import { handedBack, type HandedBackBucket } from "../handed-back-model";
import {
  bucketReadout,
  legendWords,
  scaleLabel,
  spanLabel,
} from "./handed-back-words";

const NOW = Date.parse("2026-08-18T10:00:00Z");

const clock = (ms: number) => `at ${new Date(ms).getUTCHours()}h`;

const bucket = (
  from: number,
  counts: Partial<Pick<HandedBackBucket, "ok" | "same" | "warn">>,
): HandedBackBucket => {
  const { ok = 0, same = 0, warn = 0 } = counts;
  return { from, ok, same, warn, total: ok + same + warn };
};

describe("the labels of the Today chart's scale", () => {
  it("name the unit with the right number agreement", () => {
    expect(scaleLabel(10)).toBe("10 files");
    expect(scaleLabel(1)).toBe("1 file");
    expect(scaleLabel(0.5)).toBe("0.5 files");
  });
});

describe("the Today chart's time span", () => {
  it("says how far back the oldest point is, up to now", () => {
    expect(spanLabel(handedBack([], NOW), NOW)).toBe("2 h ago – now");
  });
});

describe("the pointer's readout", () => {
  it("gives the time of the bucket and what finished in it", () => {
    const text = bucketReadout(bucket(NOW, { ok: 3 }), clock);

    expect(text).toBe("at 10h · 3 cleaned");
  });

  it("calls a file nothing was changed in already clean", () => {
    const text = bucketReadout(bucket(NOW, { ok: 3, same: 1 }), clock);

    expect(text).toBe("at 10h · 3 cleaned, 1 already clean");
  });

  it("says nothing finished in an empty bucket", () => {
    expect(bucketReadout(bucket(NOW, {}), clock)).toBe("at 10h · nothing");
  });

  it("lists each way files turned out", () => {
    expect(bucketReadout(bucket(NOW, { ok: 2, warn: 1 }), clock)).toBe(
      "at 10h · 2 cleaned, 1 need a look",
    );
  });
});

describe("the legend under the chart", () => {
  it("says a count in words, then in fewer, then as the bare number", () => {
    expect(legendWords("ok", 82)).toEqual(["82 cleaned", "82"]);
    expect(legendWords("same", 10)).toEqual([
      "10 already clean",
      "10 clean",
      "10",
    ]);
    expect(legendWords("warn", 1_250)).toEqual(["1,250 need a look", "1,250"]);
  });
});
