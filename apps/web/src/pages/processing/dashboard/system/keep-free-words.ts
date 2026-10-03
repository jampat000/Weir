import { formatBytes } from "../../../../lib/format/bytes";
import type { ProcessingLibrary } from "../../../../lib/processing/libraries-api";

const BYTES_PER_MB = 1024 * 1024;
export const NO_FREE_SPACE_LIMIT = "no limit";

/** How much room a workflow keeps free on the drive it writes to, in the units a drive is measured in: "10.00 GB", or "no limit". */
export function keepFreeWords(
  workflow: Pick<ProcessingLibrary, "minimum_free_disk_space_mb">,
): string {
  return workflow.minimum_free_disk_space_mb > 0
    ? formatBytes(workflow.minimum_free_disk_space_mb * BYTES_PER_MB)
    : NO_FREE_SPACE_LIMIT;
}
