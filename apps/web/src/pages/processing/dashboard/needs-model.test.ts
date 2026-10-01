import { describe, expect, it } from "vitest";

import type { ProcessingFile } from "../../../lib/processing/files-api";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import {
  FAILED_JOBS_LIMIT,
  STUCK_FILES_SHOWN,
  buildNeeds,
} from "./needs-model";

const workflow = {
  enabled: true,
  watched_folder: "D:/downloads/tv",
} as ProcessingLibrary;

function failedFile(id: number, reason = ""): ProcessingFile {
  return {
    id,
    relative_path: `Ember.and.Ash.S01E0${id}.mkv`,
    status: "processing_failed",
    status_reason: reason,
  } as ProcessingFile;
}

function worker(module: string, status: string, detail: string) {
  return {
    module,
    status,
    detail,
    active_workers: 0,
    expected_workers: 1,
    stale_workers: 0,
    stopped_workers: 0,
  };
}

const healthy = {
  workflows: [workflow],
  readiness: { worker_health: [] },
  failedJobCount: 0,
  stuck: [],
  rejectedCount: 0,
};

describe("what needs a person", () => {
  it("is nothing for a healthy install", () => {
    expect(buildNeeds(healthy)).toEqual([]);
  });

  it("asks for a workflow to watch when none is on, but not while they are still loading", () => {
    const [need] = buildNeeds({
      ...healthy,
      workflows: [{ ...workflow, enabled: false } as ProcessingLibrary],
    });

    expect(need.title).toBe("Nothing to watch yet");
    expect(need.link.to).toBe("/settings?tab=libraries");
    expect(buildNeeds({ ...healthy, workflows: undefined })).toEqual([]);
  });

  it("says background work has stopped, with the reason the server gave", () => {
    const [need] = buildNeeds({
      ...healthy,
      readiness: {
        worker_health: [
          worker(
            "processing",
            "degraded",
            "No worker has taken a job for 20 minutes.",
          ),
          worker("other", "ok", ""),
        ],
      },
    });

    expect(need.title).toBe("Background work has stopped");
    expect(need.reason).toBe("No worker has taken a job for 20 minutes.");
  });

  it("counts failed jobs, and stops counting at the limit", () => {
    expect(buildNeeds({ ...healthy, failedJobCount: 1 })[0].title).toBe(
      "1 job failed",
    );
    expect(buildNeeds({ ...healthy, failedJobCount: 2 })[0].title).toBe(
      "2 jobs failed",
    );
    expect(
      buildNeeds({ ...healthy, failedJobCount: FAILED_JOBS_LIMIT })[0].title,
    ).toBe(`${FAILED_JOBS_LIMIT}+ jobs failed`);
  });

  it("gives each stuck file a row with its reason and a way to try it again", () => {
    const [need] = buildNeeds({
      ...healthy,
      stuck: [
        failedFile(2, "The new file would not play. The original is safe."),
      ],
    });

    expect(need.title).toBe("Ember and Ash S01E02");
    expect(need.reason).toBe("The new file would not play.");
    expect(need.retry?.id).toBe(2);
    expect(need.link.to).toBe("/history?q=Ember.and.Ash.S01E02.mkv");
  });

  it("says what is true when a stuck file has no reason of its own", () => {
    const [need] = buildNeeds({ ...healthy, stuck: [failedFile(1)] });

    expect(need.reason).toBe(
      "Weir could not finish this file. The original is untouched.",
    );
  });

  it("counts the stuck files beyond the ones it lists in one line that leads to History", () => {
    const stuck = [1, 2, 3, 4, 5].map((id) => failedFile(id));

    const needs = buildNeeds({ ...healthy, stuck });

    expect(needs).toHaveLength(STUCK_FILES_SHOWN + 1);
    expect(needs.at(-1)).toMatchObject({
      title: "and 2 more stuck files",
      link: { to: "/history?show=failed" },
    });
  });

  it("offers the rejected files to be checked again all at once", () => {
    const [need] = buildNeeds({ ...healthy, rejectedCount: 1 });

    expect(need.title).toBe("1 file was rejected");
    expect(need.rejectedFiles).toBe(true);
    expect(buildNeeds({ ...healthy, rejectedCount: 4 })[0].title).toBe(
      "4 files were rejected",
    );
  });

  it("puts what blocks everything first and the rejected files last", () => {
    const keys = buildNeeds({
      workflows: [{ ...workflow, enabled: false } as ProcessingLibrary],
      readiness: {
        worker_health: [worker("p", "degraded", "x")],
      },
      failedJobCount: 1,
      stuck: [failedFile(1)],
      rejectedCount: 1,
    }).map((need) => need.key);

    expect(keys).toEqual([
      "setup",
      "worker-p",
      "failed-jobs",
      "stuck-1",
      "rejected",
    ]);
  });
});
