import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  fetchProcessingJobsInspection,
  postProcessingJobCancelPending,
  postProcessingJobRecoverFinalizeFailed,
} from "./api";
import { processingKeys } from "../query-keys";

/** `recent` applies no status filter: the server returns the newest rows across all statuses. */
export type ProcessingJobsInspectionFilter =
  | "recent"
  | "pending"
  | "leased"
  | "completed"
  | "failed"
  | "handler_ok_finalize_failed"
  | "cancelled"
  | "terminal"
  /** Queued or running: what Processing shows as waiting and working. */
  | "active";

function statusesForFilter(
  filter: ProcessingJobsInspectionFilter,
): string[] | undefined {
  if (filter === "recent") {
    return undefined;
  }
  if (filter === "terminal") {
    return ["completed", "failed", "handler_ok_finalize_failed"];
  }
  if (filter === "active") {
    return ["pending", "leased"];
  }
  return [filter];
}

export function useProcessingJobsInspectionQuery(
  filter: ProcessingJobsInspectionFilter,
  limit = 100,
) {
  return useQuery({
    queryKey: processingKeys.jobsInspectionList(filter, limit),
    queryFn: () =>
      fetchProcessingJobsInspection({
        limit,
        statuses: statusesForFilter(filter),
      }),
    staleTime: 15_000,
  });
}

export function useProcessingJobCancelPendingMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (jobId: number) => postProcessingJobCancelPending(jobId),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: processingKeys.jobsInspection });
    },
  });
}

export function useProcessingJobRecoverFinalizeFailedMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (jobId: number) =>
      postProcessingJobRecoverFinalizeFailed(jobId),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: processingKeys.jobsInspection });
    },
  });
}
