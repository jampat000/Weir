import { LoadError } from "../../../../components/shared/load-error";
import { Chip } from "../../../../components/panels/chip";
import { Panel } from "../../../../components/panels/panel";
import type { useServerLogsQuery } from "../../../../lib/settings/queries";
import type {
  ServerLogEntry,
  ServerLogs,
} from "../../../../lib/settings/types";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { useAppDateFormatter } from "../../../../lib/ui/mm-format-date";
import { plural } from "../../../../lib/ui/mm-plural";
import { logLevelLabel, logLevelToneClass } from "./server-log-format";

/** What the card's header says about the list: how much of it shows, and how much of it is trouble. */
function logSummary(logs: ServerLogs | undefined): string {
  if (!logs) return "";
  return [
    `Showing ${logs.items.length.toLocaleString()} of ${plural(logs.total, "event", "events")}`,
    plural(logs.counts.error, "error", "errors"),
    plural(logs.counts.warning, "warning", "warnings"),
  ].join(" · ");
}

/** Where an entry came from, for someone tracing it; closed unless there is something to show. */
function TechnicalDetails({ entry }: { entry: ServerLogEntry }) {
  const facts = [
    { label: "Source", value: entry.source },
    { label: "Logger", value: entry.logger },
    { label: "Request ID", value: entry.correlation_id },
    { label: "Job ID", value: entry.job_id },
  ].filter((fact) => fact.value);
  if (facts.length === 0 && !entry.traceback) return null;
  return (
    <details className="mt-2">
      <summary className="mm-quiet-link cursor-pointer list-none">
        Technical details →
      </summary>
      <div className="mt-2 space-y-1.5 text-sm text-mm-text2">
        {facts.map((fact) => (
          <p key={fact.label}>
            <span className="font-medium text-mm-text1">{fact.label}:</span>{" "}
            {fact.value}
          </p>
        ))}
        {entry.traceback ? (
          // A traceback keeps its edge: the boundary says where the pasted text ends.
          <pre className="mm-log-traceback">{entry.traceback}</pre>
        ) : null}
      </div>
    </details>
  );
}

/** Recent server events, newest first, with their level and time. */
export function LogListSection({
  logsQ,
  filtered,
  onClearFilters,
}: {
  logsQ: ReturnType<typeof useServerLogsQuery>;
  /** Whether any filter is set, which offers clearing them. */
  filtered: boolean;
  onClearFilters: () => void;
}) {
  const formatDateTime = useAppDateFormatter();
  const entries = logsQ.data?.items ?? [];

  return (
    <Panel
      title="System events"
      headingId="suite-settings-logs-list-heading"
      headingLevel={3}
      padded
      aside={
        <>
          <button
            type="button"
            className={`${mmActionButtonClass({ variant: "secondary" })} mm-sys-btn`}
            disabled={logsQ.isFetching}
            onClick={() => void logsQ.refetch()}
          >
            {logsQ.isFetching ? "Refreshing…" : "Refresh"}
          </button>
          {filtered ? (
            <button
              type="button"
              className={`${mmActionButtonClass({ variant: "secondary" })} mm-sys-btn`}
              onClick={onClearFilters}
            >
              Clear filters
            </button>
          ) : null}
        </>
      }
      count={
        <span
          title="Recent runtime events, warnings, and failures captured by Weir."
          data-testid="suite-settings-log-summary"
        >
          {logSummary(logsQ.data)}
        </span>
      }
    >
      {logsQ.isPending ? (
        <p className="mm-quiet-note">Loading logs...</p>
      ) : logsQ.isError ? (
        <LoadError thing="the server log" error={logsQ.error} />
      ) : entries.length === 0 ? (
        <p className="mm-quiet-note">
          No system events matched the current filters.
        </p>
      ) : (
        <div className="mm-quiet-table-wrap">
          <table className="mm-quiet-table">
            <thead>
              <tr>
                <th scope="col">Event</th>
                <th scope="col">Level</th>
                <th scope="col">When</th>
              </tr>
            </thead>
            <tbody>
              {entries.map((entry) => (
                <tr key={`${entry.timestamp}-${entry.level}-${entry.message}`}>
                  <th scope="row" className="mm-quiet-table__name">
                    <span>{entry.message}</span>
                    <Chip dot={false}>{entry.component}</Chip>
                    {entry.detail ? (
                      <span className="mm-quiet-table__sub">
                        {entry.detail}
                      </span>
                    ) : null}
                    <TechnicalDetails entry={entry} />
                  </th>
                  <td
                    data-label="Level"
                    className={logLevelToneClass(entry.level)}
                  >
                    {logLevelLabel(entry.level)}
                  </td>
                  <td data-label="When" className="whitespace-nowrap">
                    <time dateTime={entry.timestamp}>
                      {formatDateTime(entry.timestamp)}
                    </time>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Panel>
  );
}
