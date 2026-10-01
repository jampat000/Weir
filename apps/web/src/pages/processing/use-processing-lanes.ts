import { useEffect, useMemo, useRef } from "react";

import { useLiveProgress } from "../../lib/activity/use-activity-stream-invalidation";
import { useProcessingFilesQuery } from "../../lib/processing/files-queries";
import { useProcessingJobsInspectionQuery } from "../../lib/processing/jobs-inspection/queries";
import { useProcessingLibrariesQuery } from "../../lib/processing/libraries-queries";
import { mergeLiveProgress } from "../../lib/processing/live-progress-merge";
import { parseAppTime } from "../../lib/ui/mm-format-date";
import {
  arrivingDeadline,
  buildLanes,
  libraryCleanWorkflowId,
  mergeWorkingFiles,
} from "./processing-model";
import { ACTIVE_JOBS_LIMIT, WORKING_FILES_QUERY } from "./working-count";

export const FILES_QUERY = { limit: 200 } as const;
/** Arriving counts down to each library's next look, which moves with every scan. */
export const LIBRARIES_REFRESH_MS = 10_000;
/** How long past a countdown's end before Weir's answer is fetched, and how often at most. */
const LOOK_OVERDUE_MS = 1500;
const LOOK_REFETCH_GAP_MS = 3000;

/**
 * The lanes' files, grouped by where each one is, with what each library knows about its next look.
 * `lanes` holds every workflow's files, which the cards leaving the board are tracked against; `scoped` holds
 * only the chosen workflow's (all of them when none is chosen), which is what the page shows.
 */
export function useLanes(workflowId: number | null = null) {
  const files = useProcessingFilesQuery(FILES_QUERY);
  const workingFiles = useProcessingFilesQuery(WORKING_FILES_QUERY);
  const libraries = useProcessingLibrariesQuery(true, LIBRARIES_REFRESH_MS);
  const activeJobs = useProcessingJobsInspectionQuery(
    "active",
    ACTIVE_JOBS_LIMIT,
  );
  const liveProgress = useLiveProgress();
  const built = useMemo(() => {
    const all = libraries.data ?? [];
    const nextLooks = new Map<number, { at: number; interval: number }>();
    for (const library of all) {
      const at = parseAppTime(library.next_look_at);
      if (at != null) {
        nextLooks.set(library.id, {
          at,
          interval: library.scan_interval_seconds,
        });
      }
    }
    const allFiles = mergeLiveProgress(
      mergeWorkingFiles(
        files.data?.files ?? [],
        workingFiles.data?.files ?? [],
      ),
      liveProgress,
    );
    const jobs = activeJobs.data?.jobs ?? [];
    const names = new Map(all.map((l) => [l.id, l.name]));
    const readyAfter = new Map(all.map((l) => [l.id, l.ready_after_seconds]));
    const build = (inFiles: typeof allFiles, inJobs: typeof jobs) =>
      buildLanes(inFiles, inJobs, names, readyAfter, nextLooks);
    const everything = build(allFiles, jobs);
    if (workflowId === null) return { lanes: everything, scoped: everything };
    const scoped = build(
      allFiles.filter((file) => file.library_id === workflowId),
      jobs.filter((job) => libraryCleanWorkflowId(job) === workflowId),
    );
    return { lanes: everything, scoped };
  }, [
    files.data,
    workingFiles.data,
    activeJobs.data,
    libraries.data,
    liveProgress,
    workflowId,
  ]);
  return { files, libraries, lanes: built.lanes, scoped: built.scoped };
}

/**
 * A countdown that has run out means Weir is looking at that file now. Its answer, picked up or held
 * again for a new reason with a new time, only reaches the screen as fresh data, and a scan that
 * changes nothing else sends no live event, so fetch it rather than keep saying "checking it now".
 */
export function useRefetchOverdueLooks(
  { files, libraries, lanes }: ReturnType<typeof useLanes>,
  now: number,
) {
  const lastRefetch = useRef(0);
  useEffect(() => {
    const due = lanes.arriving.some((item) => {
      const deadline = arrivingDeadline(item);
      return deadline != null && deadline <= now - LOOK_OVERDUE_MS;
    });
    if (!due || now - lastRefetch.current < LOOK_REFETCH_GAP_MS) return;
    lastRefetch.current = now;
    void files.refetch();
    void libraries.refetch();
  }, [now, lanes.arriving, files, libraries]);
}
