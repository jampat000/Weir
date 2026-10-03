import { FactTable, type Fact } from "../../../../components/shared/fact-table";
import { LoadError } from "../../../../components/shared/load-error";
import { useServerMetricsQuery } from "../../../../lib/settings/queries";
import {
  formatAverageMs,
  formatRuntimeUptime,
  requestIssueSummary,
} from "./server-log-format";

const LOADING = "Loading...";

/** Counters for troubleshooting, folded away: on a healthy install none of them needs reading. */
export function ServerDiagnostics() {
  const metricsQ = useServerMetricsQuery();
  const metrics = metricsQ.data;
  const requestIssues = requestIssueSummary(metrics?.status_counts);

  const facts: Fact[] = [
    {
      label: "Running for",
      value: metrics ? formatRuntimeUptime(metrics.uptime_seconds) : LOADING,
    },
    {
      label: "Requests handled",
      value: metrics ? String(metrics.total_requests) : LOADING,
    },
    {
      label: "Average response",
      value: metrics ? formatAverageMs(metrics.average_response_ms) : LOADING,
    },
    {
      label: "Logged failures",
      value: metrics ? String(metrics.error_log_count) : LOADING,
    },
    {
      label: "Request issues",
      value: metrics ? requestIssues.value : LOADING,
    },
  ];

  return (
    <details className="mm-quiet-fold mm-log-diagnostics">
      <summary className="mm-quiet-fold__head">
        <span
          id="suite-settings-diagnostics-heading"
          className="mm-quiet-fold__title"
        >
          Server diagnostics
        </span>
        <span className="mm-quiet-fold__state">
          <span className="mm-log-diagnostics__show">Show →</span>
          <span className="mm-log-diagnostics__hide">Hide →</span>
        </span>
      </summary>
      <div className="mm-quiet-fold__body">
        <p className="mm-quiet-note">
          Advanced counters for troubleshooting. Request issues usually mean a
          browser or API request was rejected or asked for something that was
          not found; they are not the same as application failures.
        </p>
        {metricsQ.isError ? (
          <div className="mt-4">
            <LoadError thing="server diagnostics" error={metricsQ.error} />
          </div>
        ) : (
          <div className="mt-4">
            <FactTable caption="Server runtime counters" facts={facts} />
          </div>
        )}
        {metrics ? (
          <p className="mt-3 text-xs text-mm-text3">{requestIssues.detail}</p>
        ) : null}
      </div>
    </details>
  );
}
