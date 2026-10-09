import { renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { ActivityRecentFilters } from "../../../../lib/api/activity-api";
import { useRecentWork } from "./use-recent-work";

const asked: ActivityRecentFilters[] = [];

vi.mock("../../../../lib/activity/queries", () => ({
  useActivityWindowQuery: (filters: ActivityRecentFilters) => {
    asked.push(filters);
    return { data: undefined };
  },
}));
vi.mock("../../../../lib/activity/use-activity-stream-invalidation", () => ({
  useActivityStreamInvalidations: vi.fn(),
}));

beforeEach(() => {
  asked.length = 0;
});

describe("the work finished in the last ten minutes", () => {
  it("counts each download once, as it stands now, so a skip or a failure it got past is not work done", () => {
    renderHook(() => useRecentWork(Date.parse("2026-10-09T10:00:00Z")));

    const passes = asked.find(
      (filters) =>
        filters.event_type === "processing.file_remux_pass_completed",
    );
    expect(passes?.current_only).toBe(true);
  });
});
