import { renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { ProcessingFile } from "../../lib/processing/files-api";
import { useActivityAttention } from "./use-activity-attention";

const needs: { files: ProcessingFile[] } = { files: [] };
const asked: (number | null | undefined)[] = [];

vi.mock("../processing/dashboard/use-needs-you", () => ({
  useNeedsYou: (workflowId: number | null | undefined) => {
    asked.push(workflowId);
    return { groups: [], ...needs };
  },
}));

const aFile = (id: number, name: string, updated: string) =>
  ({
    id,
    library_id: 1,
    library_name: "TV",
    relative_path: name,
    status: "processing_failed",
    status_reason: "",
    updated_at: updated,
  }) as ProcessingFile;

beforeEach(() => {
  needs.files = [
    aFile(1, "Old.Show.S01E01.mkv", "2025-01-01T00:00:00"),
    aFile(2, "New.Show.S01E02.mkv", "2026-08-19T00:00:00"),
  ];
  asked.length = 0;
});

describe("Activity's Needs you view", () => {
  it("lists the files newest first, whatever their age", () => {
    const { result } = renderHook(() => useActivityAttention(null, ""));

    expect(result.current.entries.map((entry) => entry.key)).toEqual([
      "download-2",
      "download-1",
    ]);
  });

  it("counts every file the badge's own source holds, whatever the search", () => {
    const { result } = renderHook(() => useActivityAttention(null, "new.show"));

    expect(result.current.count).toBe(2);
    expect(result.current.entries).toHaveLength(1);
  });

  it("narrows the files to a search, ignoring case", () => {
    const { result } = renderHook(() =>
      useActivityAttention(null, " new.show "),
    );

    expect(result.current.entries.map((entry) => entry.key)).toEqual([
      "download-2",
    ]);
  });

  it("asks for the workflow it is narrowed to", () => {
    renderHook(() => useActivityAttention(3, ""));

    expect(new Set(asked)).toEqual(new Set([3]));
  });
});
