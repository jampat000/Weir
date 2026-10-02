import { describe, expect, it } from "vitest";

import { fullCaptionLines, type MeasureText } from "./caption-fit";

/** Every character is 6px wide, so a line's width is easy to see. */
const SIX_PX: MeasureText = (text) => text.length * 6;

const cleaned = {
  outcome: "1 audio removed",
  what: "1 audio removed",
  saved: "−365 MB",
  savedAgo: "−365 MB · 1 min ago",
};
const already = {
  outcome: "Already right · nothing to change",
  what: "Already right",
  saved: null,
  savedAgo: "1 min ago",
};

describe("what the full caption says at the poster's width", () => {
  it("says the outcome, the saving and when whole, where they fit", () => {
    // 19 characters is 114px: it fits 116px, and the outcome wraps on two lines.
    expect(fullCaptionLines(cleaned, 116, SIX_PX)).toEqual({
      outcome: "1 audio removed",
      last: "−365 MB · 1 min ago",
    });
  });

  it("keeps the saving alone, and leaves the time to the tooltip and name, where the two do not fit", () => {
    expect(fullCaptionLines(cleaned, 100, SIX_PX)?.last).toBe("−365 MB");
  });

  it("counts a line that only just fills the poster as not fitting", () => {
    // 19 characters is 114px.
    expect(fullCaptionLines(cleaned, 114, SIX_PX)?.last).toBe("−365 MB");
    expect(fullCaptionLines(cleaned, 115, SIX_PX)?.last).toBe(
      "−365 MB · 1 min ago",
    );
  });

  it("gives up the tile's full caption where even the saving alone does not fit", () => {
    // 7 characters is 42px.
    expect(fullCaptionLines(cleaned, 42, SIX_PX)).toBeNull();
    expect(fullCaptionLines(cleaned, 43, SIX_PX)).not.toBeNull();
  });

  it("does not show a time alone for a file that saved something", () => {
    // The saving, 8 characters, needs 48px; the time alone would fit 47px, but must not stand for it.
    const spaced = {
      ...cleaned,
      saved: "−1.32 GB",
      savedAgo: "−1.32 GB · 1 min",
    };
    expect(fullCaptionLines(spaced, 47, SIX_PX)).toBeNull();
  });

  it("says a file's whole outcome sentence when it wraps within two lines, and the short words when it does not", () => {
    // "Already right ·" is 15 characters and "nothing to change" 17: 102px.
    expect(fullCaptionLines(already, 103, SIX_PX)?.outcome).toBe(
      "Already right · nothing to change",
    );
    expect(fullCaptionLines(already, 101, SIX_PX)?.outcome).toBe(
      "Already right",
    );
  });

  it("says only when for a file that saved nothing", () => {
    expect(fullCaptionLines(already, 103, SIX_PX)?.last).toBe("1 min ago");
  });

  it("ends the short outcome in an ellipsis, not a third line, where even it needs one", () => {
    // "Already" is 42px: at 55px "Already right" takes two lines, and the clamp ends a longer outcome there too.
    expect(fullCaptionLines(already, 55, SIX_PX)?.outcome).toBe(
      "Already right",
    );
    expect(
      fullCaptionLines(
        {
          ...cleaned,
          outcome: "4 audio, 12 subtitles removed",
          what: "4 audio, 12 subtitles removed",
        },
        50,
        SIX_PX,
      )?.outcome,
    ).toBe("4 audio, 12 subtitles removed");
  });
});
