import { describe, expect, it } from "vitest";

import { withStatusCounts, type LibraryOverview } from "./library-mode-api";

function overviewWith(totals: Record<string, unknown>): LibraryOverview {
  return { totals } as unknown as LibraryOverview;
}

describe("withStatusCounts", () => {
  it("counts no file in any status when the server sends no per-status counts", () => {
    const { totals } = withStatusCounts(overviewWith({ files: 12 }));

    expect(totals.files).toBe(12);
    expect(totals.by_status).toEqual({
      needs_cleaning: 0,
      cleaning: 0,
      matches: 0,
      cant_clean_yet: 0,
      left_alone: 0,
    });
  });

  it("leaves the counts the server sent as they are", () => {
    const sent = overviewWith({
      files: 5,
      by_status: {
        needs_cleaning: 3,
        cleaning: 1,
        matches: 1,
        cant_clean_yet: 0,
        left_alone: 0,
      },
    });

    expect(withStatusCounts(sent)).toBe(sent);
  });
});
