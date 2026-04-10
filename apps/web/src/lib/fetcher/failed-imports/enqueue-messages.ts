import type { FailedImportEnqueueOut } from "./types";

/** After recording a manual Radarr/Sonarr failed-import pass request. */

export function failedImportEnqueueResultMessage(out: FailedImportEnqueueOut): string {
  if (out.enqueue_outcome === "created") {
    return "Recorded — a new failed-import pass row was added.";
  }
  return "Already recorded — the existing failed-import pass row was reused (no duplicate).";
}
