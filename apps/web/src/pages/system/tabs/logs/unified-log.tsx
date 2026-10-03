import { useMemo, useState } from "react";

import { useTableColumns } from "../../../../lib/ui/use-table-columns";
import { LogCard } from "./log-card";
import { LOG_COLUMNS, logSortQuery } from "./log-columns";
import { LogHeaderFilters } from "./log-header-filters";
import { EMPTY_LOG_FILTERS } from "./log-filters";
import { useLogEntries } from "./use-log-entries";
import { useLogFilters } from "./use-log-filters";
import { useLeftoverChoicesDropped } from "./use-log-source-change";

/**
 * System › Logs: everything Weir recorded, events, jobs and its server log, as one list. The filters are on the header's
 * title line and in the address; the list is the Log card under it. A row opens in place, and while one is open the list
 * does not move, so what is being read stays put.
 */
export function UnifiedLog({ onOpenSettings }: { onOpenSettings: () => void }) {
  const {
    filters,
    query: filtered,
    typed,
    setTyped,
    change,
    clear,
  } = useLogFilters();
  const columns = useTableColumns(LOG_COLUMNS);
  const query = useMemo(
    () => ({ ...filtered, ...logSortQuery(columns.sort) }),
    [filtered, columns.sort],
  );
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const { watchFeed, ...read } = useLogEntries(query, expandedId !== null);
  const dropping = useLeftoverChoicesDropped(
    filters,
    read.counts,
    !read.loading,
    change,
  );
  const entries = dropping ? { ...read, loading: true } : read;

  return (
    <LogHeaderFilters
      filters={filters}
      counts={entries.counts}
      busy={entries.loading}
      search={typed}
      onSearch={setTyped}
      onChange={change}
    >
      {(movedPickers) => (
        <LogCard
          movedPickers={movedPickers}
          filters={filters}
          query={query}
          columns={columns}
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
