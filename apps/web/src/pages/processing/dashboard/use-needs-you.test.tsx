import { renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { ProcessingFile } from "../../../lib/processing/files-api";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import { useNeedsYou } from "./use-needs-you";

const workflows = [
  { id: 1, name: "TV", enabled: true, watched_folder: "D:/tv" },
  { id: 2, name: "Movies", enabled: true, watched_folder: "D:/movies" },
] as ProcessingLibrary[];
const readiness = { worker_health: [] as unknown[] };
const failedJobs = { jobs: [] as unknown[] };
const needFiles = { files: [] as ProcessingFile[] };

vi.mock("../../../lib/activity/use-activity-stream-invalidation", () => ({
  useActivityStreamInvalidations: vi.fn(),
}));
vi.mock("../../../lib/processing/libraries-queries", () => ({
  useProcessingLibrariesQuery: () => ({ data: workflows }),
}));
vi.mock("../../../lib/system/readiness-queries", () => ({
  useSystemReadinessQuery: () => ({ data: readiness }),
}));
vi.mock("../../../lib/processing/jobs-inspection/queries", () => ({
  useProcessingJobsInspectionQuery: () => ({ data: failedJobs }),
}));
vi.mock("./needs-files", () => ({
  useNeedsFiles: () => needFiles.files,
}));

function file(overrides: Partial<ProcessingFile>): ProcessingFile {
  return {
    id: 1,
    library_id: 1,
    library_name: "TV",
    relative_path: "Ember.and.Ash.S01E02.mkv",
    status: "processing_failed",
    status_reason: "",
    failure_class: "execution",
    ...overrides,
  } as ProcessingFile;
}

beforeEach(() => {
  readiness.worker_health = [];
  failedJobs.jobs = [];
  needFiles.files = [];
});

describe("what needs a person, for the panel, the Today tile and the sidebar", () => {
  it("counts the failed, held, skipped and rejected files and the problems with Weir in one number", () => {
    failedJobs.jobs = [{ id: 1 }];
    needFiles.files = [
      file({ id: 1 }),
      file({
        id: 2,
        status: "rejected",
        failure_class: "rules",
        status_reason: "Rejected: none of its audio tracks are in English.",
      }),
      file({ id: 3, status: "on_hold", hold_until: null }),
      file({
        id: 4,
        status: "skipped",
        status_reason: "Skipped because its path matches an exclude pattern.",
      }),
    ];

    const { result } = renderHook(() => useNeedsYou(null));

    expect(result.current.count).toBe(5);
  });

  it("counts what the groups hold, including the files past the rows a group lists", () => {
    needFiles.files = [1, 2, 3, 4, 5, 6].map((id) => file({ id }));

    const { result } = renderHook(() => useNeedsYou(null));

    expect(result.current.groups).toHaveLength(1);
    expect(result.current.count).toBe(6);
  });

  it("leaves a file that only waits its turn out of the count", () => {
    needFiles.files = [
      file({ id: 1, status: "on_hold", hold_until: "2026-10-02T12:00:00Z" }),
    ];

    const { result } = renderHook(() => useNeedsYou(null));

    expect(result.current.count).toBe(0);
  });

  it("counts a kind of work's own files, and always what is wrong with Weir itself, so the Today tile and the panel agree", () => {
    failedJobs.jobs = [
      { id: 1, job_kind: "processing.file.remux_pass.v1" },
      { id: 2, job_kind: "processing.library.clean.v1" },
    ];
    readiness.worker_health = [
      { module: "processing", status: "degraded", detail: "Stopped." },
    ];
    needFiles.files = [file({ id: 1 }), file({ id: 2 })];

    const counts = (filter: "all" | "download" | "library") =>
      renderHook(() => useNeedsYou(null, filter)).result.current.count;

    expect(counts("all")).toBe(4);
    expect(counts("download")).toBe(4);
    expect(counts("library")).toBe(2);
  });

  it("counts only the chosen workflow's files, and still what is wrong with Weir itself", () => {
    failedJobs.jobs = [{ id: 1 }];
    needFiles.files = [
      file({ id: 1, library_id: 1 }),
      file({ id: 2, library_id: 2 }),
    ];

    const { result } = renderHook(() => useNeedsYou(2));

    expect(result.current.count).toBe(2);
  });
});
