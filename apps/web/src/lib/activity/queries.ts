import { useQuery } from "@tanstack/react-query";
import { fetchActivityRecent } from "../api/activity-api";

export const activityRecentKey = ["activity", "recent"] as const;

export function useActivityRecentQuery() {
  return useQuery({
    queryKey: activityRecentKey,
    queryFn: fetchActivityRecent,
    staleTime: 15_000,
  });
}
