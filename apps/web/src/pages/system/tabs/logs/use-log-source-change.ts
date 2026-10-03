import { useEffect, useState } from "react";

import type { SystemLogPage } from "../../../../lib/system/system-log-api";
import { choicesLeftEmpty } from "./log-choices";
import type { LogFilters } from "./log-filters";

/**
 * When the source changes, drops the chosen categories and levels the new source has no rows for, once its counts have
 * arrived. Returns whether it is about to, so the list can keep waiting rather than show an empty answer that is about to
 * be replaced.
 */
export function useLeftoverChoicesDropped(
  filters: LogFilters,
  counts: SystemLogPage["counts"] | undefined,
  settled: boolean,
  change: (next: Partial<LogFilters>) => void,
): boolean {
  const [checkedSource, setCheckedSource] = useState(filters.source);
  const due =
    settled && counts !== undefined && checkedSource !== filters.source;
  const left = due ? choicesLeftEmpty(filters, counts) : null;

  useEffect(() => {
    if (!due) return;
    if (left) change(left);
    setCheckedSource(filters.source);
    // `left` is worked out from the same values `due` is, so they are the only things to wait on.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [due, filters.source]);

  return left !== null;
}
