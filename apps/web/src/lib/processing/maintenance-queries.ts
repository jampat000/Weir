import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import {
  fetchProcessingMaintenance,
  runProcessingMaintenance,
  type MaintenanceFamily,
  type MaintenanceState,
} from "./maintenance-api";
import { fetchProcessingRuntimeSettings } from "./runtime-settings-api";
import type { ProcessingRuntimeSettingsOut } from "./types";
import { processingKeys } from "./query-keys";

export function useProcessingMaintenanceQuery() {
  return useQuery<MaintenanceState>({
    queryKey: processingKeys.maintenance,
    queryFn: fetchProcessingMaintenance,
    // A queued sweep starts within seconds, so the panel has to notice without a reload.
    refetchInterval: 15_000,
  });
}

export function useRunProcessingMaintenance() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      family,
      mediaScope,
    }: {
      family: MaintenanceFamily;
      mediaScope: "movie" | "tv";
    }) => runProcessingMaintenance(family, mediaScope),
    onSuccess: () =>
      void qc.invalidateQueries({ queryKey: processingKeys.maintenance }),
  });
}

export function useProcessingRuntimeSettingsQuery() {
  return useQuery<ProcessingRuntimeSettingsOut>({
    queryKey: processingKeys.runtimeSettings,
    queryFn: fetchProcessingRuntimeSettings,
  });
}
