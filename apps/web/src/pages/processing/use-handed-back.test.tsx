import { renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { ActivityRecentFilters } from "../../lib/api/activity-api";
import { useHandedBack } from "./use-handed-back";

const asked: ActivityRecentFilters[] = [];

vi.mock("../../lib/activity/queries", () => ({
  useActivityWindowQuery: (filters: ActivityRecentFilters) => {
    asked.push(filters);
    return { data: undefined };
  },
}));

beforeEach(() => {
  asked.length = 0;
});

describe("the last two hours of finished files", () => {
  it("counts each download once, as it stands now, the way Just finished lists it", () => {
    renderHook(() => useHandedBack("all", Date.parse("2026-10-09T10:00:00Z")));

    const passes = asked.find(
      (filters) =>
        filters.event_type === "processing.file_remux_pass_completed",
    );
    expect(passes?.current_only).toBe(true);
  });
});
