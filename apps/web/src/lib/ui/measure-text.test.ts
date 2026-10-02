import { describe, expect, it } from "vitest";

import { fittingWords, lineWords, type MeasureText } from "./measure-text";

/** Every character is 6px wide, so a line's width is easy to see. */
const SIX_PX: MeasureText = (text) => text.length * 6;

const SAVED = ["221 MB saved", "221 MB"];
const REMOVED = ["−1 audio · −7 subs", "−8 tracks"];

describe("what a caption line says in the room it has", () => {
  it("says the fullest words, where they fit", () => {
    // 12 characters is 72px, and a pixel is left over for the type's rounding.
    expect(lineWords(SAVED, 73, SIX_PX)).toBe("221 MB saved");
    expect(lineWords(REMOVED, 109, SIX_PX)).toBe("−1 audio · −7 subs");
  });

  it("counts words that only just fill the room as not fitting", () => {
    expect(lineWords(SAVED, 72, SIX_PX)).toBe("221 MB");
    expect(lineWords(REMOVED, 108, SIX_PX)).toBe("−8 tracks");
  });

  it("falls back to the shortest words, cut by an ellipsis, where none fit", () => {
    expect(lineWords(SAVED, 20, SIX_PX)).toBe("221 MB");
    expect(lineWords(REMOVED, 20, SIX_PX)).toBe("−8 tracks");
  });

  it("falls back to no words at all, the dot alone, where even the shortest do not fit", () => {
    const short = ["221 MB", ""];

    expect(lineWords(short, 37, SIX_PX)).toBe("221 MB");
    expect(lineWords(short, 36, SIX_PX)).toBe("");
    expect(lineWords(short, 0, SIX_PX)).toBe("");
  });

  it("says a single set of words as it is, whatever the room", () => {
    expect(lineWords(["Rejected"], 200, SIX_PX)).toBe("Rejected");
    expect(lineWords(["Rejected"], 20, SIX_PX)).toBe("Rejected");
  });
});

describe("which words fit at all", () => {
  it("is the fullest that fit, and nothing where none does", () => {
    expect(fittingWords(SAVED, 73, SIX_PX)).toBe("221 MB saved");
    expect(fittingWords(SAVED, 50, SIX_PX)).toBe("221 MB");
    expect(fittingWords(SAVED, 30, SIX_PX)).toBeUndefined();
  });
});
