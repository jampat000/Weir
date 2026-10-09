import { renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { ActivityRecentFilters } from "../../lib/api/activity-api";
import { useFinishedFiles } from "./use-finished-files";

type Asked = { filters: ActivityRecentFilters; enabled: boolean };
const asked: Asked[] = [];

vi.mock("../../lib/activity/queries", () => ({
  useActivityRecentQuery: (
    filters: ActivityRecentFilters,
    { enabled = true }: { enabled?: boolean } = {},
  ) => {
    asked.push({ filters, enabled });
    return { data: undefined };
  },
}));

const PASSES = "processing.file_remux_pass_completed";
const CLEANS = "library.file_cleaned";

/** What the hook asked for each kind of entry on its latest render. */
function askedFor(eventType: string): Asked {
  const latest = [...asked]
    .reverse()
    .find((ask) => ask.filters.event_type === eventType);
  if (!latest) throw new Error(`Nothing asked for ${eventType}.`);
  return latest;
}

beforeEach(() => {
  asked.length = 0;
});

describe("the newest finished files", () => {
  it("asks for both kinds across every workflow on Everything", () => {
    renderHook(() => useFinishedFiles());

    expect(askedFor(PASSES)).toMatchObject({ enabled: true });
    expect(askedFor(CLEANS)).toMatchObject({ enabled: true });
    expect(askedFor(PASSES).filters.library_id).toBeUndefined();
  });

  it("asks for each download once, as it stands now, so a failure it has got past is not listed", () => {
    renderHook(() => useFinishedFiles());

    expect(askedFor(PASSES).filters).toMatchObject({
      known_files_only: true,
      current_only: true,
    });
    expect(askedFor(CLEANS).filters.current_only).toBeUndefined();
  });

  it("asks only for new downloads' entries for new downloads", () => {
    renderHook(() => useFinishedFiles("download"));

    expect(askedFor(PASSES).enabled).toBe(true);
    expect(askedFor(CLEANS).enabled).toBe(false);
  });

  it("asks only for a library's cleans for library cleaning, and for as many as it asks of passes", () => {
    renderHook(() => useFinishedFiles("library"));

    expect(askedFor(PASSES).enabled).toBe(false);
    expect(askedFor(CLEANS).enabled).toBe(true);
    expect(askedFor(CLEANS).filters.limit).toBe(askedFor(PASSES).filters.limit);
  });

  it("asks the server for the one workflow's entries when the page is narrowed to it", () => {
    renderHook(() => useFinishedFiles("all", 4));

    expect(askedFor(PASSES).filters.library_id).toBe(4);
    expect(askedFor(CLEANS).filters.library_id).toBe(4);
  });
});
