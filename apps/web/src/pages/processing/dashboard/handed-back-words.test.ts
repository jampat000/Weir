import { describe, expect, it } from "vitest";

import { handedBack, type HandedBackBucket } from "../handed-back-model";
import { bucketReadout, scaleLabel, spanLabels } from "./handed-back-words";

const NOW = Date.parse("2026-08-18T10:00:00Z");

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

describe("the ends of the Today chart's time axis", () => {
  it("say how far back the oldest point is, and now", () => {
    expect(spanLabels(handedBack([], NOW), NOW)).toEqual(["2 h ago", "now"]);
  });
});

describe("the pointer's readout", () => {
  it("gives the time of the bucket and what finished in it", () => {
    const text = bucketReadout(bucket(NOW, { ok: 3 }));

    expect(text).toMatch(/^\d{1,2}[:.]\d{2}.* · 3 cleaned$/);
  });

  it("says nothing finished in an empty bucket", () => {
    expect(bucketReadout(bucket(NOW, {}))).toMatch(/ · nothing$/);
  });

  it("lists each way files turned out", () => {
    expect(bucketReadout(bucket(NOW, { ok: 2, warn: 1 }))).toMatch(
      / · 2 cleaned, 1 need a look$/,
    );
  });
});
