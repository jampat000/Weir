/** Addresses into Activity: one file in the group it belongs to, so a link lands on the file and not just on the page. */
import type { ProcessingFile } from "../../lib/processing/files-api";
import { activityGroupOf, type ActivityGroup } from "./activity-entries";

export const ACTIVITY_PATH = "/activity";

/** The period that has every file Activity keeps, so a file last touched long ago is still in the list. */
const EVERYTHING_KEPT = "all";

/** One group's view in Activity, narrowed to one workflow when there is one: "Failed" for the files that failed. */
export function activityGroupPath(
  group: ActivityGroup,
  workflowId?: number | null,
): string {
  const params = new URLSearchParams({ show: group });
  if (workflowId != null) params.set("library", String(workflowId));
  return `${ACTIVITY_PATH}?${params}`;
}

/**
 * One file in Activity: its group's view with the file chosen, however long ago it was last touched. Activity's
 * own actions for it (Choose tracks, Why is this held, Pass through unchanged, Process now) are in its detail.
 */
export function activityFilePath(file: ProcessingFile): string {
  const params = new URLSearchParams();
  const group = activityGroupOf(file);
  if (group) params.set("show", group);
  params.set("file", String(file.id));
  params.set("within", EVERYTHING_KEPT);
  return `${ACTIVITY_PATH}?${params}`;
}
