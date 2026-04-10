import type { ManualCleanupDriveEnqueueOut } from "./types";

/** User-facing line after queueing a failed-import download-queue pass (per library app). */

export function manualCleanupEnqueueResultMessage(out: ManualCleanupDriveEnqueueOut): string {
  if (out.enqueue_outcome === "created") {
    return "Queued now — a new failed-import pass was recorded.";
  }
  return "Already queued — the existing failed-import pass was reused (no duplicate).";
}
