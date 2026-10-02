import { HeaderSearch } from "../../../../components/shell/header-search";
import {
  LogsPicker,
  LogsViewControls,
  useSearchCollapsed,
} from "./logs-header-controls";
import { LOG_LEVEL_OPTIONS, type LogLevelFilter } from "./server-log-format";

export type LogSearch = {
  text: string;
  level: LogLevelFilter;
  tracebacksOnly: boolean;
};

export const EMPTY_LOG_SEARCH: LogSearch = {
  text: "",
  level: "",
  tracebacksOnly: false,
};

const ENTRY_KINDS = [
  { value: "all", label: "All entries" },
  { value: "tracebacks", label: "Tracebacks only" },
];

/** Whether any of the server log's filters is set. */
export function anyLogFilterSet(search: LogSearch): boolean {
  return Boolean(search.text.trim() || search.level || search.tracebacksOnly);
}

/**
 * The server log's filters, on the header's title line after "Show". Level and tracebacks apply as they are chosen;
 * the search waits for a pause in typing, which the page handles.
 */
export function ServerLogFilters({
  search,
  onChange,
}: {
  search: LogSearch;
  onChange: (next: LogSearch) => void;
}) {
  const collapsed = useSearchCollapsed();
  return (
    <LogsViewControls>
      <HeaderSearch
        label="Search the server log"
        placeholder="Search the log"
        className="mm-history-search-box"
        collapsed={collapsed}
        value={search.text}
        onChange={(event) => onChange({ ...search, text: event.target.value })}
      />
      <LogsPicker
        label="Level"
        options={LOG_LEVEL_OPTIONS}
        value={search.level}
        onChange={(level) =>
          onChange({ ...search, level: level as LogLevelFilter })
        }
      />
      <LogsPicker
        label="Entries"
        options={ENTRY_KINDS}
        value={search.tracebacksOnly ? "tracebacks" : "all"}
        onChange={(kind) =>
          onChange({ ...search, tracebacksOnly: kind === "tracebacks" })
        }
      />
    </LogsViewControls>
  );
}
