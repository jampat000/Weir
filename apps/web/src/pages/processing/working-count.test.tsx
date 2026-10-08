import { renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ProcessingFile } from "../../lib/processing/files-api";
import type { ProcessingJobInspectionRow } from "../../lib/processing/jobs-inspection/types";
import { processingKeys } from "../../lib/processing/query-keys";
import { LIBRARY_CLEAN_JOB_KIND, countWorking } from "./processing-model";
import {
  ACTIVE_JOBS_LIMIT,
  WORKING_FILES_QUERY,
  useWorkingCount,
} from "./working-count";

const lists = {
  files: [] as ProcessingFile[],
  jobs: [] as ProcessingJobInspectionRow[],
};
const followStream = vi.fn();

vi.mock("../../lib/activity/use-activity-stream-invalidation", () => ({
  useActivityStreamInvalidations: (keys: unknown, options: unknown) =>
    followStream(keys, options),
}));
vi.mock("../../lib/processing/files-queries", () => ({
  useProcessingFilesQuery: () => ({ data: { files: lists.files } }),
}));
vi.mock("../../lib/processing/jobs-inspection/queries", () => ({
  useProcessingJobsInspectionQuery: () => ({ data: { jobs: lists.jobs } }),
}));

function file(
  id: number,
  status: ProcessingFile["status"],
  progressStatus: string | null = null,
): ProcessingFile {
  return {
    id,
    status,
    library_id: 1,
    library_name: "TV",
    relative_path: `Show.S01E0${id}.mkv`,
    progress_status: progressStatus,
    progress_percent: null,
  } as ProcessingFile;
}

function libraryClean(
  id: number,
  status: ProcessingJobInspectionRow["status"],
): ProcessingJobInspectionRow {
  return {
    id,
    job_kind: LIBRARY_CLEAN_JOB_KIND,
    status,
    payload_json: JSON.stringify({ library_id: 1, path: `Film ${id}.mkv` }),
  } as ProcessingJobInspectionRow;
}

describe("the count of working files", () => {
  it("counts the files a pass is writing and leaves out ones held back", () => {
    const files = [
      file(1, "processing", "processing"),
      file(2, "on_hold"),
      file(3, "unprocessed"),
    ];

    expect(countWorking(files, [])).toBe(1);
  });

  it("leaves out a file on its final checks, which the page shows under Handing back", () => {
    const files = [
      file(1, "processing", "processing"),
      file(2, "processing", "finishing"),
    ];

    expect(countWorking(files, [])).toBe(1);
  });

  it("adds a library clean that is running and not one that is only queued", () => {
    const jobs = [libraryClean(10, "leased"), libraryClean(11, "pending")];

    expect(countWorking([file(1, "processing")], jobs)).toBe(2);
  });
});

describe("useWorkingCount", () => {
  beforeEach(() => {
    lists.files = [file(1, "processing"), file(2, "on_hold")];
    lists.jobs = [libraryClean(10, "leased")];
    followStream.mockReset();
  });

  it("gives what the Working lane counts from the lane's own lists", () => {
    const { result } = renderHook(() => useWorkingCount());

    expect(result.current).toBe(2);
  });

  it("reads both lists again whenever the stream says a file or a job changed, with no timer", () => {
    renderHook(() => useWorkingCount());

    expect(followStream).toHaveBeenCalledWith(
      [
        processingKeys.fileList(WORKING_FILES_QUERY),
        processingKeys.jobsInspectionList("active", ACTIVE_JOBS_LIMIT),
      ],
      expect.objectContaining({ throttleMs: expect.any(Number) }),
    );
  });
});
