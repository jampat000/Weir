import type { ActivityRecentFilters } from "../api/activity-api";

/** Every Activity query key. Invalidating `recent` refreshes every list and window under it. */
export const activityKeys = {
  recent: ["activity", "recent"] as const,
  recentList: (filters?: ActivityRecentFilters) =>
    ["activity", "recent", filters ?? {}] as const,
  window: (
    filters: Omit<ActivityRecentFilters, "limit" | "before_id">,
    maxPages: number,
  ) => ["activity", "recent", "window", filters, maxPages] as const,
};
