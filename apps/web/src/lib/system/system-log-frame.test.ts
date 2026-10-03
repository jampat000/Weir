import { describe, expect, it } from "vitest";

import { parseSystemLogFrame } from "./system-log-frame";

describe("parseSystemLogFrame", () => {
  it("reads a line's time, level and message", () => {
    const data = JSON.stringify({
      at: "2026-10-02T10:00:00Z",
      level: "WARNING",
      message: "Radarr was slow.",
    });
    expect(parseSystemLogFrame(data)).toEqual({
      at: "2026-10-02T10:00:00Z",
      level: "WARNING",
      message: "Radarr was slow.",
    });
  });

  it("is null for a level the log does not write to the stream", () => {
    expect(
      parseSystemLogFrame(
        JSON.stringify({ at: "x", level: "INFO", message: "m" }),
      ),
    ).toBeNull();
  });

  it("is null when a part is missing", () => {
    expect(
      parseSystemLogFrame(JSON.stringify({ at: "x", level: "ERROR" })),
    ).toBeNull();
  });

  it("is null for a frame that is not JSON", () => {
    expect(parseSystemLogFrame("not json")).toBeNull();
  });
});
