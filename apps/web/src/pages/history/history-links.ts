/** Addresses into History: one file in the group it belongs to, so a link lands on the file and not just on the page. */
import type { ProcessingFile } from "../../lib/processing/files-api";
import { historyGroupOf, type HistoryGroup } from "./history-entries";

export const HISTORY_PATH = "/history";

/** The period that has every file History keeps, so a file last touched long ago is still in the list. */
const EVERYTHING_KEPT = "all";

/** One group's view in History, narrowed to one workflow when there is one: "Failed" for the files that failed. */
export function historyGroupPath(
  group: HistoryGroup,
  workflowId?: number | null,
): string {
  const params = new URLSearchParams({ show: group });
  if (workflowId != null) params.set("library", String(workflowId));
  return `${HISTORY_PATH}?${params}`;
}

/**
 * One file in History: its group's view with the file chosen, however long ago it was last touched. History's
 * own actions for it (Choose tracks, Why is this held, Pass through unchanged, Process now) are in its detail.
 */
export function historyFilePath(file: ProcessingFile): string {
  const params = new URLSearchParams();
  const group = historyGroupOf(file);
  if (group) params.set("show", group);
  params.set("file", String(file.id));
  params.set("within", EVERYTHING_KEPT);
  return `${HISTORY_PATH}?${params}`;
}
