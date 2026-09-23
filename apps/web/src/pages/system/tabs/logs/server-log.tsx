import { useState } from "react";

import { FactTable, type Fact } from "../../../../components/shared/fact-table";
import { useServerLogsQuery } from "../../../../lib/settings/queries";
import { LogListSection } from "./log-list-section";
import {
  EMPTY_LOG_SEARCH,
  LogSearchSection,
  type LogSearch,
} from "./log-search-section";
import { ServerDiagnostics } from "./server-diagnostics";
import { SERVER_LOG_PAGE_SIZE } from "./server-log-format";

/** Weir's own server log (System › Logs › Server log): counts, diagnostics, search and the entries. */
export function ServerLog() {
  const [search, setSearch] = useState<LogSearch>(EMPTY_LOG_SEARCH);
  const logsQ = useServerLogsQuery({
    level: search.level || undefined,
    search: search.text.trim() || undefined,
    has_exception: search.tracebacksOnly ? true : undefined,
    limit: SERVER_LOG_PAGE_SIZE,
  });
  const logs = logsQ.data;

  // Coequal counters: on a healthy install the one worth reading is the one that is usually zero,
  // so none of them is a hero (docs/design/content-language.md, rule 2).
  const counts: Fact[] = [
    { label: "Showing now", value: `${logs?.items.length ?? 0} events` },
    { label: "Matching events", value: `${logs?.total ?? 0} events` },
    {
      label: "Errors",
      value: String(logs?.counts.error ?? 0),
      toneClass: (logs?.counts.error ?? 0) > 0 ? "mm-status-text--failed" : "",
    },
    {
      label: "Warnings",
      value: String(logs?.counts.warning ?? 0),
      toneClass:
        (logs?.counts.warning ?? 0) > 0 ? "mm-status-text--warning" : "",
    },
    { label: "Information", value: String(logs?.counts.information ?? 0) },
  ];

  return (
    <div data-testid="suite-settings-logs" className="mm-quiet-stack w-full">
      <FactTable
        caption="Log summary"
        facts={counts}
        data-testid="suite-settings-log-summary"
      />
      <ServerDiagnostics />
      <LogSearchSection
        search={search}
        onChange={setSearch}
        refreshing={logsQ.isFetching}
        onRefresh={() => void logsQ.refetch()}
      />
      <LogListSection logsQ={logsQ} />
    </div>
  );
}
