import { useCallback, useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";

import type { SystemLogQuery } from "../../../../lib/system/system-log-api";
import { useAppTimeZone } from "../../../../lib/ui/mm-format-date";
import { useDebouncedValue } from "../../../../lib/ui/use-debounced-value";
import {
  EMPTY_LOG_FILTERS,
  filtersFromParams,
  logQuery,
  paramsFromFilters,
  type LogFilters,
} from "./log-filters";

/** How long typing pauses before the search is applied. */
const SEARCH_DEBOUNCE_MS = 400;

/**
 * System › Logs' filters. They live in the address, so a view survives a reload, Back and a link; each applies as it is
 * chosen, the search once typing pauses, so there is no draft and no Apply. A time counted back from now ("Last hour")
 * counts from when it was chosen, so a list does not move as it ages.
 */
export function useLogFilters() {
  const [params, setParams] = useSearchParams();
  const timeZone = useAppTimeZone();
  const filters = useMemo(() => filtersFromParams(params), [params]);
  const [chosenAt, setChosenAt] = useState(() => Date.now());
  const [typed, setTyped] = useState(filters.text);
  const searched = useDebouncedValue(typed.trim(), SEARCH_DEBOUNCE_MS);

  const change = useCallback(
    (next: Partial<LogFilters>) => {
      if (next.when !== undefined) setChosenAt(Date.now());
      setParams(
        (previous) =>
          paramsFromFilters(
            { ...filtersFromParams(previous), ...next },
            previous,
          ),
        { replace: true },
      );
    },
    [setParams],
  );

  // What was typed becomes the filter once typing pauses; an address that changes it (a link, Back) shows in the box.
  useEffect(() => {
    if (searched !== filters.text) change({ text: searched });
    // Only the typed text should start a change: reading the address is the other direction, below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searched]);
  useEffect(() => {
    setTyped(filters.text);
  }, [filters.text]);

  const clear = useCallback(() => {
    setTyped("");
    change(EMPTY_LOG_FILTERS);
  }, [change]);

  const query: SystemLogQuery = useMemo(
    () => logQuery(filters, chosenAt, timeZone),
    [filters, chosenAt, timeZone],
  );

  return { filters, query, typed, setTyped, change, clear };
}
