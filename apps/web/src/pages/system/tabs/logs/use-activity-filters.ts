import { useMemo, useState } from "react";

import {
  EMPTY_ACTIVITY_FILTERS,
  activityLogQuery,
  type ActivityLogFilters,
} from "../../../../lib/activity/activity-filters";
import { useDebouncedValue } from "../../../../lib/ui/use-debounced-value";
import { rangeFor, type When } from "./activity-header-filters";

/** How long typing pauses before the search is applied. */
const SEARCH_DEBOUNCE_MS = 400;

/**
 * The event log's filters. Each applies as it is chosen, the search once typing pauses, so there is no draft and no
 * Apply: what the header shows is what the list is for.
 */
export function useActivityFilters() {
  const [typed, setTyped] = useState("");
  const [chosen, setChosen] = useState<ActivityLogFilters>(
    EMPTY_ACTIVITY_FILTERS,
  );
  const [when, setWhen] = useState<When>("any");
  const search = useDebouncedValue(typed.trim(), SEARCH_DEBOUNCE_MS);
  const applied = useMemo(() => ({ ...chosen, search }), [chosen, search]);
  const query = useMemo(() => activityLogQuery(applied), [applied]);

  return {
    applied,
    query,
    typed,
    setTyped,
    chosen,
    change: (next: Partial<ActivityLogFilters>) =>
      setChosen((previous) => ({ ...previous, ...next })),
    when,
    chooseWhen: (next: When) => {
      setWhen(next);
      // Picking one's own dates keeps what is there; each other choice sets the range, or clears it.
      if (next !== "custom") {
        setChosen((previous) => ({
          ...previous,
          ...rangeFor(next, new Date()),
        }));
      }
    },
    clear: () => {
      setTyped("");
      setChosen(EMPTY_ACTIVITY_FILTERS);
      setWhen("any");
    },
  };
}
