import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  fetchProcessingOperatorSettings,
  putProcessingOperatorSettings,
} from "./operator-settings-api";
import { fetchProcessingFilesAtOnce } from "./files-at-once-api";
import { fetchProcessingOverviewStats } from "./overview-stats-api";
import { postProcessingWatchedFolderRemuxScanDispatchEnqueue } from "./watched-folder-scan-api";
import type {
  ProcessingOperatorSettingsPutBody,
  ProcessingWatchedFolderRemuxScanDispatchEnqueueBody,
} from "./types";
import { processingKeys } from "./query-keys";

/** What is running and what the waiting files are waiting for. Polled: it is a live read-out. */
export function useProcessingFilesAtOnceQuery() {
  return useQuery({
    queryKey: processingKeys.filesAtOnce,
    queryFn: () => fetchProcessingFilesAtOnce(),
    staleTime: 5_000,
    refetchInterval: 10_000,
  });
}

export function useProcessingOverviewStatsQuery(windowDays?: number) {
  return useQuery({
    queryKey: processingKeys.overviewStats(windowDays),
    queryFn: () => fetchProcessingOverviewStats(windowDays),
    staleTime: 30_000,
  });
}

export function useProcessingOperatorSettingsQuery() {
  return useQuery({
    queryKey: processingKeys.operatorSettings,
    queryFn: () => fetchProcessingOperatorSettings(),
    staleTime: 30_000,
  });
}

export function useProcessingOperatorSettingsSaveMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: ProcessingOperatorSettingsPutBody) =>
      putProcessingOperatorSettings(body),
    onSuccess: (data) => {
      qc.setQueryData(processingKeys.operatorSettings, data);
      void qc.invalidateQueries({ queryKey: processingKeys.filesAtOnce });
      void qc.invalidateQueries({
        queryKey: processingKeys.runtimeSettings,
      });
    },
  });
}

export function useProcessingWatchedFolderRemuxScanDispatchEnqueueMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: ProcessingWatchedFolderRemuxScanDispatchEnqueueBody) =>
      postProcessingWatchedFolderRemuxScanDispatchEnqueue(body),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: processingKeys.files });
      void qc.invalidateQueries({ queryKey: processingKeys.jobs });
    },
  });
}
