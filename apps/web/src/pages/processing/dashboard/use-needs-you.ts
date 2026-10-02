import { useMemo } from "react";

import { useActivityStreamInvalidations } from "../../../lib/activity/use-activity-stream-invalidation";
import { useProcessingJobsInspectionQuery } from "../../../lib/processing/jobs-inspection/queries";
import { useProcessingLibrariesQuery } from "../../../lib/processing/libraries-queries";
import { processingKeys } from "../../../lib/processing/query-keys";
import { useSystemReadinessQuery } from "../../../lib/system/readiness-queries";
import type { Filter } from "../processing-filter";
import { useNeedsFiles } from "./needs-files";
import {
  FAILED_JOBS_LIMIT,
  buildNeeds,
  failedJobsFor,
  needCount,
  type NeedGroup,
} from "./needs-model";

const FAILED_JOBS_KEYS = [
  processingKeys.jobsInspectionList("failed", FAILED_JOBS_LIMIT, true),
] as const;
/** A job fails only when a pass ends, so the list follows the stream gently. */
const STREAM_THROTTLE_MS = 3_000;

export type NeedsYou = {
  groups: NeedGroup[];
  /** How many things wait on a person: each file and each problem with Weir counts once. */
  count: number;
};

/**
 * What needs a person, narrowed to one workflow or across all of them, and to one kind of work or both. The
 * Needs you panel, the Today tile's count and the sidebar's badge all read this, so they cannot disagree; they
 * share its queries.
 */
export function useNeedsYou(
  workflowId: number | null | undefined,
  filter: Filter = "all",
): NeedsYou {
  const workflows = useProcessingLibrariesQuery();
  const readiness = useSystemReadinessQuery();
  // Leaves out a file the owner has since removed from History: this is about current problems, not a
  // record of every failure Weir has ever seen (System › Jobs keeps that).
  const failedJobs = useProcessingJobsInspectionQuery(
    "failed",
    FAILED_JOBS_LIMIT,
    true,
  );
  const files = useNeedsFiles(workflowId);
  useActivityStreamInvalidations(FAILED_JOBS_KEYS, {
    exact: true,
    throttleMs: STREAM_THROTTLE_MS,
  });
  const workflowList = workflows.data;
  const readinessData = readiness.data;
  const failedJobRows = failedJobs.data?.jobs;
  return useMemo(() => {
    const groups = buildNeeds({
      workflows: workflowList,
      workflowId,
      readiness: readinessData,
      failedJobs: failedJobsFor(failedJobRows ?? [], filter),
      filter,
      files,
    });
    return { groups, count: needCount(groups) };
  }, [workflowList, workflowId, filter, readinessData, failedJobRows, files]);
}
