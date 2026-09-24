import { QuietSection } from "../../../../components/shared/quiet-section";
import { errorMessage } from "../../../../lib/api/error-message";
import type { useServerLogsQuery } from "../../../../lib/settings/queries";
import type { ServerLogEntry } from "../../../../lib/settings/types";
import { useAppDateFormatter } from "../../../../lib/ui/mm-format-date";
import { logLevelLabel, logLevelToneClass } from "./server-log-format";

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
}: {
  logsQ: ReturnType<typeof useServerLogsQuery>;
}) {
  const formatDateTime = useAppDateFormatter();
  const entries = logsQ.data?.items ?? [];

  return (
    <QuietSection
      level={3}
      headingId="suite-settings-logs-list-heading"
      heading="System events"
    >
      <p className="mm-quiet-note">
        Recent runtime events, warnings, and failures captured by Weir.
      </p>

      {logsQ.isPending ? (
        <p className="mm-quiet-note mt-4">Loading logs...</p>
      ) : logsQ.isError ? (
        <p className="mt-4 text-sm text-mm-status-failed-text" role="alert">
          {errorMessage(logsQ.error, "Could not load logs.")}
        </p>
      ) : entries.length === 0 ? (
        <p className="mm-quiet-note mt-4">
          No system events matched the current filters.
        </p>
      ) : (
        <div className="mm-quiet-table-wrap mt-4">
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
                    <span className="mm-quiet-badge">{entry.component}</span>
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
    </QuietSection>
  );
}
