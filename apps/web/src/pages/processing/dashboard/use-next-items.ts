import { useQueries } from "@tanstack/react-query";

import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import { fetchLibraryOverview } from "../../../lib/processing/library-mode-api";
import { useProcessingMaintenanceQuery } from "../../../lib/processing/maintenance-queries";
import { processingKeys } from "../../../lib/processing/query-keys";
import { shownBy, type Filter } from "../processing-filter";
import { nextItems, type LibraryCleanRun, type NextItem } from "./next-model";

/**
 * What Weir does next on its own, from the workflows' scans, the libraries' daily clean and the cleanup timers. A library's
 * clean time moves when its scan is queued or ends or its schedule changes, and the server says so on `library_scan`.
 * Narrowed to one workflow it lists that workflow's scan and clean only: a cleanup timer belongs to no workflow.
 * Narrowed to a kind of work it lists that kind's own times, and no library's clean time is asked for when only
 * new downloads are shown.
 */
export function useNextItems(
  workflows: readonly ProcessingLibrary[] | undefined,
  workflowId: number | null = null,
  filter: Filter = "all",
): NextItem[] {
  const enabled = (workflows ?? []).filter(
    (workflow) =>
      workflow.enabled && (workflowId === null || workflow.id === workflowId),
  );
  const maintenance = useProcessingMaintenanceQuery();
  const overviews = useQueries({
    queries: enabled.map((workflow) => ({
      queryKey: processingKeys.libraryOverview(workflow.id),
      queryFn: () => fetchLibraryOverview(workflow.id),
      enabled: shownBy(filter, { source: "library" }),
    })),
  });
  const cleanRuns: LibraryCleanRun[] = enabled.map((workflow, index) => ({
    libraryId: workflow.id,
    libraryName: workflow.name,
    nextRunAt: overviews[index]?.data?.schedule.next_run_at ?? null,
  }));
  return nextItems({
    workflows: enabled,
    cleanRuns,
    cleanupJobs: workflowId === null ? (maintenance.data?.families ?? []) : [],
    filter,
  });
}
