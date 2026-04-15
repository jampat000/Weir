import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { fetchFailedImportQueueAttentionSnapshot } from "./attention-snapshot-api";
import { fetchFailedImportAutomationSummary } from "./automation-summary-api";
import {
  fetchFailedImportCleanupPolicy,
  putFailedImportCleanupPolicyMovies,
  putFailedImportCleanupPolicyTvShows,
} from "./cleanup-policy-api";
import type { FailedImportCleanupPolicyAxis } from "./types";
import { postFailedImportRadarrEnqueue, postFailedImportSonarrEnqueue } from "./manual-enqueue-api";
import { fetchFailedImportFetcherSettings } from "./settings-api";
import { postFetcherJobRecoverFinalize } from "./recover-api";

export const failedImportSettingsQueryKey = ["fetcher", "failed-imports", "settings"] as const;

export const failedImportAutomationSummaryQueryKey = ["fetcher", "failed-imports", "automation-summary"] as const;

export const failedImportQueueAttentionSnapshotQueryKey = [
  "fetcher",
  "failed-imports",
  "queue-attention-snapshot",
] as const;

export const failedImportCleanupPolicyQueryKey = ["fetcher", "failed-imports", "cleanup-policy"] as const;

export function useFailedImportQueueAttentionSnapshotQuery() {
  return useQuery({
    queryKey: failedImportQueueAttentionSnapshotQueryKey,
    queryFn: () => fetchFailedImportQueueAttentionSnapshot(),
    staleTime: 15_000,
  });
}

export function useFailedImportAutomationSummaryQuery() {
  return useQuery({
    queryKey: failedImportAutomationSummaryQueryKey,
    queryFn: () => fetchFailedImportAutomationSummary(),
    staleTime: 15_000,
  });
}

export function useFailedImportCleanupPolicyQuery() {
  return useQuery({
    queryKey: failedImportCleanupPolicyQueryKey,
    queryFn: () => fetchFailedImportCleanupPolicy(),
    staleTime: 30_000,
  });
}

export function useFailedImportCleanupPolicySaveTvMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (axis: FailedImportCleanupPolicyAxis) => putFailedImportCleanupPolicyTvShows(axis),
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: failedImportCleanupPolicyQueryKey });
      void qc.invalidateQueries({ queryKey: failedImportSettingsQueryKey });
      void qc.invalidateQueries({ queryKey: failedImportAutomationSummaryQueryKey });
    },
  });
}

export function useFailedImportCleanupPolicySaveMoviesMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (axis: FailedImportCleanupPolicyAxis) => putFailedImportCleanupPolicyMovies(axis),
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: failedImportCleanupPolicyQueryKey });
      void qc.invalidateQueries({ queryKey: failedImportSettingsQueryKey });
      void qc.invalidateQueries({ queryKey: failedImportAutomationSummaryQueryKey });
    },
  });
}

export function useFailedImportFetcherSettingsQuery() {
  return useQuery({
    queryKey: failedImportSettingsQueryKey,
    queryFn: () => fetchFailedImportFetcherSettings(),
    staleTime: 30_000,
  });
}

export function useFetcherJobRecoverFinalizeMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (jobId: number) => postFetcherJobRecoverFinalize(jobId),
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: ["fetcher", "jobs", "inspection"] });
      void qc.invalidateQueries({ queryKey: failedImportAutomationSummaryQueryKey });
      void qc.invalidateQueries({ queryKey: failedImportQueueAttentionSnapshotQueryKey });
    },
  });
}

export function useFailedImportRadarrEnqueueMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => postFailedImportRadarrEnqueue(),
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: ["fetcher", "jobs", "inspection"] });
      void qc.invalidateQueries({ queryKey: ["fetcher", "failed-imports", "settings"] });
      void qc.invalidateQueries({ queryKey: failedImportAutomationSummaryQueryKey });
      void qc.invalidateQueries({ queryKey: failedImportQueueAttentionSnapshotQueryKey });
    },
  });
}

export function useFailedImportSonarrEnqueueMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => postFailedImportSonarrEnqueue(),
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: ["fetcher", "jobs", "inspection"] });
      void qc.invalidateQueries({ queryKey: ["fetcher", "failed-imports", "settings"] });
      void qc.invalidateQueries({ queryKey: failedImportAutomationSummaryQueryKey });
      void qc.invalidateQueries({ queryKey: failedImportQueueAttentionSnapshotQueryKey });
    },
  });
}
