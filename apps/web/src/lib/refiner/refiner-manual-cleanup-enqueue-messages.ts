import type { ManualCleanupDriveEnqueueOut } from "./types";

/** User-facing result line for manual cleanup-drive enqueue (Pass 23). */

export function manualCleanupEnqueueResultMessage(out: ManualCleanupDriveEnqueueOut): string {
  if (out.enqueue_outcome === "created") {
    return "Enqueued now — a durable job row was created.";
  }
  return "Already queued — an existing cleanup-drive row was reused (same dedupe key).";
}
