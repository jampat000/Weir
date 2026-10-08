import { describe, expect, it } from "vitest";

import { parseSystemChecksFrame } from "./system-checks-frame";

describe("parseSystemChecksFrame", () => {
  it("reads the times the server last looked, and null for a look it has not made", () => {
    expect(
      parseSystemChecksFrame(
        '{"folders_checked_at":null,"readiness_checked_at":"2026-10-08T03:04:05Z"}',
      ),
    ).toEqual({
      foldersCheckedAt: null,
      readinessCheckedAt: Date.UTC(2026, 9, 8, 3, 4, 5),
    });
  });

  it("ignores anything that is not an object of times", () => {
    expect(parseSystemChecksFrame("not json")).toBeNull();
    expect(parseSystemChecksFrame("[]")).toBeNull();
    expect(parseSystemChecksFrame("null")).toBeNull();
  });
});
