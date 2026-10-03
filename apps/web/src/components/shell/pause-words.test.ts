import { describe, expect, it } from "vitest";

import { pausePillWords } from "./pause-words";

describe("what the pill says while Weir is paused", () => {
  it("says when a pause with an end lifts, then the time alone, then only that it is paused", () => {
    expect(
      pausePillWords({ full: "2 Oct 2026, 10:00 pm", clock: "10:00 pm" }),
    ).toEqual([
      "Paused · until 2 Oct 2026, 10:00 pm",
      "Paused · until 10:00 pm",
      "Paused",
    ]);
  });

  it("says a pause with no end lasts until it is resumed, then that it is manual, then only that it is paused", () => {
    expect(pausePillWords(null)).toEqual([
      "Paused · until you resume",
      "Paused · manually",
      "Paused",
    ]);
  });
});
