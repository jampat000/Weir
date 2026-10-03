import { describe, expect, it } from "vitest";

import { trackMeaning } from "./track-meaning";

describe("trackMeaning", () => {
  it("draws a kept track as done", () => {
    expect(trackMeaning(true)).toBe("done");
  });

  it("draws a removed track in the colour of removal", () => {
    expect(trackMeaning(false)).toBe("broken");
  });
});
