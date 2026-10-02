import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";

import { subscribeSystemLog } from "../../../../lib/activity/use-activity-stream-invalidation";
import { fetchServerLogs } from "../../../../lib/settings/settings-api";
import { useAppSettingsQuery } from "../../../../lib/settings/queries";
import { systemKeys } from "../../../../lib/system/query-keys";
import { useNow } from "../../../../lib/ui/use-now";
import {
  MOST_LOG_LINES,
  lineFromEntry,
  lineFromFrame,
  mergeLines,
  todayCounts,
  type LogCounts,
  type LogLine,
} from "./log-card-model";

/** The log is read again this often, so lines the stream does not carry (information) and any it missed appear. */
const LOG_REFRESH_MS = 60_000;
/** Today's counts are worked out again this often, so they roll over at midnight. */
const COUNTS_TICK_MS = 60_000;

/** The log levels read on their own, so today's errors and warnings are counted past the information that fills the file. */
type ReadLevel = "" | "ERROR" | "WARNING";

function useLogLines(level: ReadLevel) {
  return useQuery({
    queryKey: systemKeys.logLines(level),
    queryFn: async () => {
      const logs = await fetchServerLogs({
        level: level || undefined,
        limit: MOST_LOG_LINES,
      });
      return logs.items.flatMap((entry) => lineFromEntry(entry) ?? []);
    },
    staleTime: LOG_REFRESH_MS,
    refetchInterval: LOG_REFRESH_MS,
    retry: false,
  });
}

export type SystemLog = {
  /** Newest first. */
  lines: LogLine[];
  counts: LogCounts;
  /** The first read has not answered yet. */
  loading: boolean;
  /** Every read failed, and nothing has arrived on the stream. */
  failed: boolean;
};

/**
 * Weir's log lines for the Log card: the first read of the log, kept current by the warnings and errors the stream
 * pushes as they are written, and today's count of each in the timezone chosen in Settings.
 */
export function useSystemLog(): SystemLog {
  const [live, setLive] = useState<LogLine[]>([]);
  useEffect(
    () =>
      subscribeSystemLog((frame) => {
        const line = lineFromFrame(frame, Date.now());
        if (line) setLive((current) => mergeLines([[line], current]));
      }),
    [],
  );
  const everything = useLogLines("");
  const errors = useLogLines("ERROR");
  const warnings = useLogLines("WARNING");
  const timeZone = useAppSettingsQuery().data?.app_timezone || undefined;
  const now = useNow(COUNTS_TICK_MS);
  const lines = useMemo(
    () =>
      mergeLines([
        live,
        errors.data ?? [],
        warnings.data ?? [],
        everything.data ?? [],
      ]),
    [live, errors.data, warnings.data, everything.data],
  );
  const counts = useMemo(
    () => todayCounts(lines, now, timeZone),
    [lines, now, timeZone],
  );
  const reads = [everything, errors, warnings];
  return {
    lines,
    counts,
    loading: reads.some((read) => read.isPending),
    failed: reads.every((read) => read.isError) && live.length === 0,
  };
}
