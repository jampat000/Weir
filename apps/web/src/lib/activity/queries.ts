import { useQuery } from "@tanstack/react-query";
import {
  fetchActivityRecent,
  type ActivityRecentFilters,
} from "../api/activity-api";

import { activityKeys } from "./query-keys";

export function useActivityRecentQuery(filters?: ActivityRecentFilters) {
  return useQuery({
    queryKey: activityKeys.recentList(filters),
    queryFn: () => fetchActivityRecent(filters),
    staleTime: 15_000,
  });
}

/** Every entry in a window, not only the first page: what a chart counts from must not stop at 100. */
export type ActivityWindow = {
  items: NonNullable<Awaited<ReturnType<typeof fetchActivityRecent>>["items"]>;
  /** The server's count of every entry that matches, whether or not all were fetched. */
  total: number;
  /** False only when the window held more than `maxPages` pages. */
  complete: boolean;
};

export function useActivityWindowQuery(
  filters: Omit<ActivityRecentFilters, "limit" | "before_id">,
  maxPages = 5,
) {
  return useQuery({
    queryKey: activityKeys.window(filters, maxPages),
    queryFn: async (): Promise<ActivityWindow> => {
      const items: ActivityWindow["items"] = [];
      let total = 0;
      let beforeId: number | undefined;
      for (let page = 0; page < maxPages; page++) {
        const response = await fetchActivityRecent({
          ...filters,
          limit: 100,
          before_id: beforeId,
        });
        const got = response.items ?? [];
        if (page === 0) total = response.total;
        items.push(...got);
        if (!response.has_more || got.length === 0)
          return { items, total, complete: true };
        beforeId = got[got.length - 1].id;
      }
      return { items, total, complete: false };
    },
    staleTime: 15_000,
  });
}
