import { useState } from "react";

import { LogCard } from "./log-card";
import { LogHeaderFilters } from "./log-header-filters";
import { EMPTY_LOG_FILTERS } from "./log-filters";
import { useLogEntries } from "./use-log-entries";
import { useLogFilters } from "./use-log-filters";

/**
 * System › Logs: everything Weir recorded, events, jobs and its server log, as one list. The filters are on the header's
 * title line and in the address; the list is the Log card under it. A row opens in place, and while one is open the list
 * does not move, so what is being read stays put.
 */
export function UnifiedLog({ onOpenSettings }: { onOpenSettings: () => void }) {
  const { filters, query, typed, setTyped, change, clear } = useLogFilters();
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const { watchFeed, ...entries } = useLogEntries(query, expandedId !== null);

  return (
    <LogHeaderFilters
      filters={filters}
      counts={entries.counts}
      search={typed}
      onSearch={setTyped}
      onChange={change}
    >
      {(movedPickers) => (
        <LogCard
          movedPickers={movedPickers}
          filters={filters}
          query={query}
          entries={entries}
          watchFeed={watchFeed}
          expandedId={expandedId}
          onToggle={(id) => setExpandedId((open) => (open === id ? null : id))}
          onChange={change}
          onClearFilters={clear}
          onOpenSettings={onOpenSettings}
          onRelated={(job) => {
            setExpandedId(null);
            setTyped("");
            change({ ...EMPTY_LOG_FILTERS, job });
          }}
        />
      )}
    </LogHeaderFilters>
  );
}
