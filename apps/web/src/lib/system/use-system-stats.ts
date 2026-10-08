import { useEffect } from "react";
import {
  useQuery,
  useQueryClient,
  type UseQueryResult,
} from "@tanstack/react-query";

import {
  subscribeSystemOverview,
  subscribeSystemStats,
} from "../activity/use-activity-stream-invalidation";
import { fetchSystemOverview, fetchSystemStats } from "./system-stats-api";
import { withFrame } from "./system-stats-model";
import { systemKeys } from "./query-keys";
import type { SystemOverview, SystemStats } from "./system-stats-types";

/**
 * The last ten minutes of readings, the machine's facts and the drives. Each `system.stats` frame is added by
 * {@link useSystemStatsFrames}; a stream that was away reads the history whole again when it is back.
 */
export function useSystemStatsQuery(): UseQueryResult<SystemStats> {
  return useQuery({
    queryKey: systemKeys.stats,
    queryFn: fetchSystemStats,
    // Frames reach the cache only while the page listens, so a page that opens reads the history again whatever it kept.
    staleTime: 0,
  });
}

/**
 * Follows the stream's `system.stats` frames: each adds its point to the history and replaces the reading, the machine's
 * facts and the drives, so the traces move once a second without a request. Used once, by the page that shows them.
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

/** How Weir itself is doing: read once, then replaced by each `system.overview` frame the server sends when a fact changes. */
export function useSystemOverviewQuery(): UseQueryResult<SystemOverview> {
  const client = useQueryClient();
  useEffect(
    () =>
      subscribeSystemOverview((overview) =>
        client.setQueryData<SystemOverview>(systemKeys.overview, overview),
      ),
    [client],
  );
  return useQuery({
    queryKey: systemKeys.overview,
    queryFn: fetchSystemOverview,
    // Frames reach the cache only while a card is listening, so a card that opens reads the overview again whatever it kept.
    staleTime: 0,
  });
}
