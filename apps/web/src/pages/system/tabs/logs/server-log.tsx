import { useState } from "react";

import { useServerLogsQuery } from "../../../../lib/settings/queries";
import { useDebouncedValue } from "../../../../lib/ui/use-debounced-value";
import { DownloadLogSection } from "./download-log-section";
import { LogListSection } from "./log-list-section";
import {
  EMPTY_LOG_SEARCH,
  ServerLogFilters,
  anyLogFilterSet,
  type LogSearch,
} from "./server-log-filters";
import { ServerDiagnostics } from "./server-diagnostics";
import { SERVER_LOG_PAGE_SIZE } from "./server-log-format";

/** How long typing pauses before the search text itself is sent; level and tracebacks apply at once. */
const SEARCH_TEXT_DEBOUNCE_MS = 400;

/** Weir's own server log (System › Logs › Server log): the entries, then diagnostics and the whole file. */
export function ServerLog() {
  const [search, setSearch] = useState<LogSearch>(EMPTY_LOG_SEARCH);
  const debouncedText = useDebouncedValue(
    search.text.trim(),
    SEARCH_TEXT_DEBOUNCE_MS,
  );
  const logsQ = useServerLogsQuery({
    level: search.level || undefined,
    search: debouncedText || undefined,
    has_exception: search.tracebacksOnly ? true : undefined,
    limit: SERVER_LOG_PAGE_SIZE,
  });

  return (
    <div data-testid="suite-settings-logs" className="mm-sys-stack">
      <ServerLogFilters search={search} onChange={setSearch} />
      <LogListSection
        logsQ={logsQ}
        filtered={anyLogFilterSet(search)}
        onClearFilters={() => setSearch(EMPTY_LOG_SEARCH)}
      />
      <ServerDiagnostics />
      <DownloadLogSection />
    </div>
  );
}
