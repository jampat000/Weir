/** Addresses into History: one file in the group it belongs to, so a link lands on the file and not just on the page. */
import type { ProcessingFile } from "../../lib/processing/files-api";
import { historyGroupOf } from "./history-entries";

export const HISTORY_PATH = "/history";

/** The period that has every file History keeps, so a file last touched long ago is still in the list. */
const EVERYTHING_KEPT = "all";

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
