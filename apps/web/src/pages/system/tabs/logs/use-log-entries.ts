import {
  keepPreviousData,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { useEffect, useMemo, useState } from "react";

import {
  subscribeSystemLog,
  useActivityStreamInvalidations,
} from "../../../../lib/activity/use-activity-stream-invalidation";
import {
  fetchSystemLog,
  type SystemLogPage,
  type SystemLogQuery,
  type SystemLogRow,
} from "../../../../lib/system/system-log-api";
import { systemKeys } from "../../../../lib/system/query-keys";
import { useCalmFeed } from "./use-calm-feed";

/** Rows in a page, and the most a click on "Load more" adds. */
export const LOG_PAGE_SIZE = 50;

/** Slow enough that a burst of events does not read the log again for each one. */
const LIVE_THROTTLE_MS = 3_000;

/** The log is read again this often when no frame has said anything changed: jobs change without an event. */
const BACKSTOP_MS = 30_000;

const LOG_KEYS = [systemKeys.logEntriesAll] as const;

type OlderRows = {
  key: string;
  rows: SystemLogRow[];
  /** Where the next page starts; null once the oldest row is in. */
  cursor: string | null;
};

/** Rows by id, the first of any id winning, so a row on two pages is listed once. */
function uniqueRows(
  ...lists: readonly (readonly SystemLogRow[])[]
): SystemLogRow[] {
  const seen = new Set<string>();
  return lists.flat().filter((row) => !seen.has(row.id) && seen.add(row.id));
}

/** How many of the newest rows `live` has that `shown` does not: the ones that arrived since the list was last updated. */
function arrivedSince(live: SystemLogPage, shown: SystemLogPage): number {
  const known = new Set(shown.items.map((row) => row.id));
  const firstKnown = live.items.findIndex((row) => known.has(row.id));
  return firstKnown === -1 ? live.items.length : firstKnown;
}

/** A warning or error written to the server log arrives on the stream: the log is read again, as events do. */
function useLogStreamRefresh(): void {
  const queryClient = useQueryClient();
  useActivityStreamInvalidations(LOG_KEYS, { throttleMs: LIVE_THROTTLE_MS });
  useEffect(() => {
    let timer: number | null = null;
    const unsubscribe = subscribeSystemLog(() => {
      if (timer !== null) return;
      timer = window.setTimeout(() => {
        timer = null;
        void queryClient.invalidateQueries(
          { queryKey: systemKeys.logEntriesAll },
          { cancelRefetch: false },
        );
      }, LIVE_THROTTLE_MS);
    });
    return () => {
      unsubscribe();
      if (timer !== null) window.clearTimeout(timer);
    };
  }, [queryClient]);
}

/**
 * System › Logs for a set of filters: the newest page, kept current by the stream, and older pages on request. New rows land
 * in the list only while the reader is at the top with nothing open; otherwise they wait behind a count, so the row being
 * read does not move. A change of filter starts again from the newest.
 */
export function useLogEntries(query: SystemLogQuery, holding: boolean) {
  const dataKey = useMemo(() => JSON.stringify(query), [query]);
  const [older, setOlder] = useState<OlderRows | null>(null);
  const [loadingOlder, setLoadingOlder] = useState(false);
  const [olderError, setOlderError] = useState<string | null>(null);
  useLogStreamRefresh();

  const live = useQuery({
    queryKey: systemKeys.logEntries(query),
    queryFn: () => fetchSystemLog({ ...query, limit: LOG_PAGE_SIZE }),
    placeholderData: keepPreviousData,
    refetchInterval: BACKSTOP_MS,
    staleTime: LIVE_THROTTLE_MS,
  });
  const olderForQuery = older?.key === dataKey ? older : null;
  const { watchFeed, shown, show } = useCalmFeed(
    live.isPlaceholderData ? undefined : live.data,
    dataKey,
    holding || olderForQuery !== null,
  );

  const page = shown ?? live.data;
  const rows = page ? uniqueRows(page.items, olderForQuery?.rows ?? []) : [];
  const nextCursor = olderForQuery
    ? olderForQuery.cursor
    : (page?.next_cursor ?? null);
  const fresh = live.isPlaceholderData ? undefined : live.data;

  async function loadMore(): Promise<void> {
    if (!nextCursor || loadingOlder) return;
    setLoadingOlder(true);
    setOlderError(null);
    try {
      const next = await fetchSystemLog({
        ...query,
        cursor: nextCursor,
        limit: LOG_PAGE_SIZE,
      });
      setOlder({
        key: dataKey,
        rows: uniqueRows(olderForQuery?.rows ?? [], next.items),
        cursor: next.next_cursor,
      });
    } catch {
      setOlderError("Could not load older entries. Try again.");
    } finally {
      setLoadingOlder(false);
    }
  }

  return {
    watchFeed,
    rows,
    /** What the filters match across every page, and what each choice of a filter would show: always the newest reading. */
    total: (fresh ?? page)?.total ?? 0,
    counts: (fresh ?? page)?.counts,
    /** Nothing has been read for these filters yet. */
    loading: page === undefined || live.isPlaceholderData,
    error: live.isError && page === undefined ? live.error : null,
    olderError,
    /** Rows that arrived while the list was held still. */
    arrived: page && fresh ? arrivedSince(fresh, page) : 0,
    showArrived: () => {
      setOlder(null);
      if (fresh) show(fresh);
    },
    hasMore: nextCursor !== null,
    loadingMore: loadingOlder,
    loadMore: () => void loadMore(),
    /** Shows the newest page again at once, whatever the reader is doing, such as after clearing it. */
    reload: async () => {
      setOlder(null);
      const refreshed = await live.refetch();
      if (refreshed.data) show(refreshed.data);
    },
  };
}

/** What a read of the log holds, apart from the element it is watched through. */
export type LogEntries = Omit<ReturnType<typeof useLogEntries>, "watchFeed">;
