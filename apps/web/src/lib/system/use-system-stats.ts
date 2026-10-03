import { useEffect } from "react";
import {
  useQuery,
  useQueryClient,
  type UseQueryResult,
} from "@tanstack/react-query";

import { subscribeSystemStats } from "../activity/use-activity-stream-invalidation";
import { fetchSystemOverview, fetchSystemStats } from "./system-stats-api";
import { withFrame } from "./system-stats-model";
import { systemKeys } from "./query-keys";
import type { SystemOverview, SystemStats } from "./system-stats-types";

/** Drives, the machine's uptime and the history are read again this often; the stream carries the seconds between. */
const STATS_REFRESH_MS = 30_000;
/** How Weir itself is doing changes slowly: its jobs, requests and checks are read again this often. */
const OVERVIEW_REFRESH_MS = 10_000;

/**
 * The last ten minutes of readings and the machine's drives. Each `system.stats` frame is added by
 * {@link useSystemStatsFrames}; the history is read whole again every 30 seconds, so a stream that was away catches up.
 */
export function useSystemStatsQuery(): UseQueryResult<SystemStats> {
  return useQuery({
    queryKey: systemKeys.stats,
    queryFn: fetchSystemStats,
    refetchInterval: STATS_REFRESH_MS,
    staleTime: STATS_REFRESH_MS,
  });
}

/**
 * Follows the stream's `system.stats` frames: each adds its point to the history and replaces the reading, so
 * the traces move once a second without a request. Used once, by the page that shows them.
 */
export function useSystemStatsFrames(): void {
  const client = useQueryClient();
  useEffect(
    () =>
      subscribeSystemStats((frame) =>
        client.setQueryData<SystemStats>(systemKeys.stats, (stats) =>
          stats ? withFrame(stats, frame) : stats,
        ),
      ),
    [client],
  );
}

export function useSystemOverviewQuery(): UseQueryResult<SystemOverview> {
  return useQuery({
    queryKey: systemKeys.overview,
    queryFn: fetchSystemOverview,
    refetchInterval: OVERVIEW_REFRESH_MS,
    staleTime: OVERVIEW_REFRESH_MS,
  });
}
