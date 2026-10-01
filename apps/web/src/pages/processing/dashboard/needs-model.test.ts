import { describe, expect, it } from "vitest";

import type { ProcessingFile } from "../../../lib/processing/files-api";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import {
  FAILED_JOBS_LIMIT,
  FILES_SHOWN_PER_GROUP,
  buildNeeds,
  needCount,
} from "./needs-model";

const workflow = {
  enabled: true,
  watched_folder: "D:/downloads/tv",
} as ProcessingLibrary;

function failedFile(
  id: number,
  overrides: Partial<ProcessingFile> = {},
): ProcessingFile {
  return {
    id,
    library_id: 1,
    library_name: "TV",
    relative_path: `Ember.and.Ash.S01E0${id}.mkv`,
    status: "processing_failed",
    status_reason: "",
    failure_class: "execution",
    ...overrides,
  } as ProcessingFile;
}

function rejectedFile(
  id: number,
  overrides: Partial<ProcessingFile> = {},
): ProcessingFile {
  return failedFile(id, {
    status: "rejected",
    failure_class: "rules",
    status_reason: "Rejected: none of its audio tracks are in English.",
    ...overrides,
  });
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
  workflowId: null,
  readiness: { worker_health: [] },
  failedJobCount: 0,
  files: [],
};

describe("what needs a person", () => {
  it("is nothing for a healthy install", () => {
    expect(buildNeeds(healthy)).toEqual([]);
  });

  it("asks for a workflow to watch when none is on, but not while they are still loading", () => {
    const [group] = buildNeeds({
      ...healthy,
      workflows: [{ ...workflow, enabled: false } as ProcessingLibrary],
    });

    expect(group.rows[0].title).toBe("Nothing to watch yet");
    expect(group.rows[0].link?.to).toBe("/settings?tab=libraries");
    expect(buildNeeds({ ...healthy, workflows: undefined })).toEqual([]);
  });

  it("says background work has stopped, with the reason the server gave", () => {
    const [group] = buildNeeds({
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

    expect(group.rows).toHaveLength(1);
    expect(group.rows[0].title).toBe("Background work has stopped");
    expect(group.rows[0].reason).toBe(
      "No worker has taken a job for 20 minutes.",
    );
  });

  it("counts failed jobs, and stops counting at the limit", () => {
    const title = (failedJobCount: number) =>
      buildNeeds({ ...healthy, failedJobCount })[0].rows[0].title;

    expect(title(1)).toBe("1 job failed");
    expect(title(2)).toBe("2 jobs failed");
    expect(title(FAILED_JOBS_LIMIT)).toBe(`${FAILED_JOBS_LIMIT}+ jobs failed`);
  });

  it("groups what is wrong with Weir itself under one title", () => {
    const [group] = buildNeeds({
      ...healthy,
      failedJobCount: 2,
      readiness: { worker_health: [worker("p", "degraded", "x")] },
    });

    expect(group.title).toBe("2 things to fix in Weir");
  });
});

describe("the files that need a person", () => {
  it("gives each file a row with its name, its reason and the file itself for its actions", () => {
    const [group] = buildNeeds({
      ...healthy,
      files: [
        failedFile(2, {
          status_reason: "The new file would not play. The original is safe.",
        }),
      ],
    });

    expect(group.rows[0]).toMatchObject({
      title: "Ember and Ash S01E02",
      reason: "The new file would not play.",
      file: { id: 2 },
    });
  });

  it("says what is true when a file has no reason of its own", () => {
    const [group] = buildNeeds({ ...healthy, files: [failedFile(1)] });

    expect(group.rows[0].reason).toBe(
      "Weir could not finish this file. The original is untouched.",
    );
  });

  it("titles each group by how many files share a reason, in plain words", () => {
    const groups = buildNeeds({
      ...healthy,
      files: [
        failedFile(1),
        failedFile(2, { failure_class: "preflight" }),
        failedFile(3, { failure_class: "guardrail" }),
        failedFile(4, { failure_class: null }),
        rejectedFile(5),
        rejectedFile(6),
        rejectedFile(7, { status_reason: "Rejected: no keepable track." }),
        rejectedFile(8, {
          failure_class: "execution",
          status_reason: "Rejected for a replacement.",
        }),
      ],
    });

    expect(groups.map((group) => group.title)).toEqual([
      "1 failed while writing",
      "1 did not pass the checks",
      "1 stopped to keep the original safe",
      "1 failed",
      "2 not in a language you keep",
      "1 turned down by your rules",
      "1 rejected for a replacement",
    ]);
  });

  it("lists a file held with no clock on it as stuck, but not one counting down to its turn", () => {
    const stuck = failedFile(1, {
      status: "on_hold",
      hold_until: null,
      status_reason: "Weir could not open this file for reading.",
    });
    const settling = failedFile(2, {
      status: "on_hold",
      hold_until: "2026-10-02T12:00:00Z",
    });

    const groups = buildNeeds({ ...healthy, files: [stuck, settling] });

    expect(groups.map((group) => group.title)).toEqual(["1 stuck"]);
    expect(groups[0].rows[0].file?.id).toBe(1);
  });

  it("lists a file skipped by one of the workflow's rules, but not a routine skip", () => {
    const byRule = failedFile(1, {
      status: "skipped",
      status_reason:
        "Skipped because its path matches this workflow's exclude patterns.",
    });
    const routine = failedFile(2, {
      status: "skipped",
      status_reason: "Already matched the workflow's rules.",
    });

    const groups = buildNeeds({ ...healthy, files: [routine, byRule] });

    expect(groups.map((group) => group.title)).toEqual([
      "1 skipped by a workflow rule",
    ]);
    expect(groups[0].rows[0].file?.id).toBe(1);
  });

  it("leaves a file that is waiting its turn or already finished out altogether", () => {
    const groups = buildNeeds({
      ...healthy,
      files: [
        failedFile(1, { status: "unprocessed" }),
        failedFile(2, { status: "processed" }),
      ],
    });

    expect(groups).toEqual([]);
  });

  it("lists the groups in a fixed order whatever order the files arrive in", () => {
    const keys = buildNeeds({
      ...healthy,
      files: [
        failedFile(1, {
          status: "skipped",
          status_reason: "Skipped because its path matches an exclude pattern.",
        }),
        rejectedFile(2),
        failedFile(3, { status: "on_hold", hold_until: null }),
        failedFile(4),
      ],
    }).map((group) => group.key);

    expect(keys).toEqual([
      "failed-writing",
      "stuck",
      "rejected-language",
      "skipped-by-rule",
    ]);
  });

  it("offers Process all again only on the rejected groups", () => {
    const groups = buildNeeds({
      ...healthy,
      files: [failedFile(1), rejectedFile(2)],
    });

    expect(groups.map((group) => group.rejected)).toEqual([false, true]);
  });

  it("lists a few files per group and counts the rest, which History has", () => {
    const files = [1, 2, 3, 4, 5, 6].map((id) => failedFile(id));

    const [group] = buildNeeds({ ...healthy, files });

    expect(group.title).toBe("6 failed while writing");
    expect(group.rows).toHaveLength(FILES_SHOWN_PER_GROUP);
    expect(group.more).toBe(2);
  });

  it("narrows to one workflow's files, and leaves out what is wrong with Weir itself", () => {
    const groups = buildNeeds({
      ...healthy,
      workflowId: 2,
      failedJobCount: 3,
      files: [
        failedFile(1, { library_id: 1 }),
        failedFile(2, { library_id: 2 }),
        rejectedFile(3, { library_id: 1 }),
      ],
    });

    expect(groups).toHaveLength(1);
    expect(groups[0].rows.map((row) => row.file?.id)).toEqual([2]);
  });

  it("puts what blocks everything first and the rejected files last", () => {
    const keys = buildNeeds({
      ...healthy,
      workflows: [{ ...workflow, enabled: false } as ProcessingLibrary],
      readiness: { worker_health: [worker("p", "degraded", "x")] },
      failedJobCount: 1,
      files: [failedFile(1), rejectedFile(2)],
    }).map((group) => group.key);

    expect(keys).toEqual(["weir", "failed-writing", "rejected-language"]);
  });

  it("counts every file and every problem once, including the ones past a group's rows", () => {
    const groups = buildNeeds({
      ...healthy,
      failedJobCount: 1,
      files: [1, 2, 3, 4, 5, 6].map((id) => failedFile(id)),
    });

    expect(needCount(groups)).toBe(7);
  });
});
