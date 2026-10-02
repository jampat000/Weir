/** What the full Health view says about disk space, beyond what the compact panel shows. */
import { formatBytes } from "../../../lib/format/bytes";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";

const BYTES_PER_MB = 1024 * 1024;
export const NO_FREE_SPACE_LIMIT = "no limit";

export type DiskRow = {
  key: number;
  workflow: string;
  outputFolder: string;
  /** What Weir keeps free there: "10 GB", or "no limit". */
  keepFree: string;
};

/** Each workflow's output drive and how much room Weir keeps free on it. */
export function diskRows(workflows: readonly ProcessingLibrary[]): DiskRow[] {
  return workflows.map((workflow) => ({
    key: workflow.id,
    workflow: workflow.name,
    outputFolder: workflow.output_folder,
    keepFree:
      workflow.minimum_free_disk_space_mb > 0
        ? formatBytes(workflow.minimum_free_disk_space_mb * BYTES_PER_MB)
        : NO_FREE_SPACE_LIMIT,
  }));
}
