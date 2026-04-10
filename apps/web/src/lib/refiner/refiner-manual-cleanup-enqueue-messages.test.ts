import { describe, expect, it } from "vitest";
import { manualCleanupEnqueueResultMessage } from "./refiner-manual-cleanup-enqueue-messages";
import type { ManualCleanupDriveEnqueueOut } from "./types";

describe("manualCleanupEnqueueResultMessage", () => {
  it("distinguishes created vs already_present", () => {
    const created: ManualCleanupDriveEnqueueOut = {
      job_id: 1,
      dedupe_key: "k",
      job_kind: "j",
      enqueue_outcome: "created",
    };
    const dup: ManualCleanupDriveEnqueueOut = { ...created, enqueue_outcome: "already_present" };
    expect(manualCleanupEnqueueResultMessage(created)).toContain("Enqueued now");
    expect(manualCleanupEnqueueResultMessage(dup)).toContain("Already queued");
    expect(manualCleanupEnqueueResultMessage(dup)).toContain("no duplicate row");
  });
});
