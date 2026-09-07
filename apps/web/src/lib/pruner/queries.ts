import { useQuery } from "@tanstack/react-query";
import {
  fetchPrunerInstance,
  fetchPrunerInstances,
  fetchPrunerJobsInspection,
  fetchPrunerOverviewStats,
  fetchPrunerStudios,
} from "./api";

export const prunerInstancesQueryKey = ["pruner", "instances"] as const;
export const prunerOverviewStatsQueryKey = [
  "pruner",
  "overview-stats",
] as const;
export const prunerJobsInspectionQueryKey = (limit = 100) =>
  ["pruner", "jobs-inspection", limit] as const;

export function usePrunerInstancesQuery() {
  return useQuery({
    queryKey: prunerInstancesQueryKey,
    queryFn: fetchPrunerInstances,
  });
}

export function usePrunerOverviewStatsQuery() {
  return useQuery({
    queryKey: prunerOverviewStatsQueryKey,
    queryFn: fetchPrunerOverviewStats,
    staleTime: 30_000,
  });
}

export function usePrunerInstanceQuery(instanceId: number) {
  return useQuery({
    queryKey: ["pruner", "instances", instanceId],
    queryFn: () => fetchPrunerInstance(instanceId),
    enabled: Number.isFinite(instanceId) && instanceId > 0,
  });
}

export function usePrunerJobsInspectionQuery(limit = 100) {
  return useQuery({
    queryKey: prunerJobsInspectionQueryKey(limit),
    queryFn: () => fetchPrunerJobsInspection(limit),
  });
}

export function usePrunerStudiosQuery(
  instanceId: number,
  scope: "tv" | "movies",
) {
  return useQuery({
    queryKey: ["pruner", "instances", instanceId, "studios", scope],
    queryFn: () => fetchPrunerStudios(instanceId, scope),
    enabled: Number.isFinite(instanceId) && instanceId > 0,
    staleTime: 5 * 60 * 1000,
  });
}
