import { useQueries } from "@tanstack/react-query";

import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import { fetchLibraryOverview } from "../../../lib/processing/library-mode-api";
import { useProcessingMaintenanceQuery } from "../../../lib/processing/maintenance-queries";
import { processingKeys } from "../../../lib/processing/query-keys";
import { nextItems, type LibraryCleanRun, type NextItem } from "./next-model";

/** A library's clean time only moves when it runs or its schedule is changed, so a minute is soon enough. */
const CLEAN_SCHEDULE_REFRESH_MS = 60_000;

/** What Weir does next on its own, from the workflows' scans, the libraries' daily clean and the cleanup timers. */
export function useNextItems(
  workflows: readonly ProcessingLibrary[] | undefined,
): NextItem[] {
  const enabled = (workflows ?? []).filter((workflow) => workflow.enabled);
  const maintenance = useProcessingMaintenanceQuery();
  const overviews = useQueries({
    queries: enabled.map((workflow) => ({
      queryKey: processingKeys.libraryOverview(workflow.id),
      queryFn: () => fetchLibraryOverview(workflow.id),
      staleTime: CLEAN_SCHEDULE_REFRESH_MS,
      refetchInterval: CLEAN_SCHEDULE_REFRESH_MS,
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
    cleanupJobs: maintenance.data?.families ?? [],
  });
}
