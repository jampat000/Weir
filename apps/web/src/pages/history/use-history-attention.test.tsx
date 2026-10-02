import { renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { ProcessingFile } from "../../lib/processing/files-api";
import type { NeedRow } from "../processing/dashboard/needs-model";
import { useHistoryAttention } from "./use-history-attention";

const needs: { files: ProcessingFile[]; weir: NeedRow[]; count: number } = {
  files: [],
  weir: [],
  count: 0,
};
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
  needs.weir = [];
  needs.count = 2;
  asked.length = 0;
});

describe("History's Needs you view", () => {
  it("lists the files newest first, whatever their age", () => {
    const { result } = renderHook(() => useHistoryAttention(null, ""));

    expect(result.current.entries.map((entry) => entry.key)).toEqual([
      "download-2",
      "download-1",
    ]);
  });

  it("takes its count, and what is wrong with Weir, from the badge's own source", () => {
    needs.count = 3;
    needs.weir = [{ key: "worker-p", title: "Stopped", reason: "x" }];

    const { result } = renderHook(() => useHistoryAttention(null, ""));

    expect(result.current.count).toBe(3);
    expect(result.current.weir).toEqual(needs.weir);
  });

  it("narrows the files to a search, ignoring case", () => {
    const { result } = renderHook(() =>
      useHistoryAttention(null, " new.show "),
    );

    expect(result.current.entries.map((entry) => entry.key)).toEqual([
      "download-2",
    ]);
  });

  it("asks for the workflow it is narrowed to", () => {
    renderHook(() => useHistoryAttention(3, ""));

    expect(new Set(asked)).toEqual(new Set([3]));
  });
});
