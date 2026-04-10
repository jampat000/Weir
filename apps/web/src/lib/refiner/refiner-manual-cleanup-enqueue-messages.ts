import type { ManualCleanupDriveEnqueueOut } from "./types";

/** User-facing result line after queueing an import-cleanup check (movies or TV app). */

export function manualCleanupEnqueueResultMessage(out: ManualCleanupDriveEnqueueOut): string {
  if (out.enqueue_outcome === "created") {
    return "Queued now — a new import-cleanup check was recorded.";
  }
  return "Already queued — the existing import-cleanup check was reused (no duplicate queue row).";
}
