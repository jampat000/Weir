import { useMemo } from "react";

import { useNeedsYou } from "../processing/dashboard/use-needs-you";
import { historyEntries, type HistoryEntry } from "./history-entries";

export type HistoryAttention = {
  /** The files that wait on a person, however long ago they were last touched, newest first. */
  entries: HistoryEntry[];
  /** How many files wait on a person, as the sidebar's badge counts them. */
  count: number;
};

/**
 * History's Needs you view: the same files the Dashboard's Needs you panel and the sidebar's badge count, from the
 * same source, so the three cannot disagree. What is wrong with Weir itself is not a file and is not here. It is not limited to a period, since a file that
 * needs a person needs them however long ago it stopped. A search narrows the files, as it does every view.
 */
export function useHistoryAttention(
  workflowId: number | null,
  search: string,
): HistoryAttention {
  const { files } = useNeedsYou(workflowId);
  const needle = search.trim().toLowerCase();
  const entries = useMemo(
    () =>
      historyEntries(
        files.filter((file) =>
          file.relative_path.toLowerCase().includes(needle),
        ),
        [],
      ),
    [files, needle],
  );
  return { entries, count: files.length };
}
