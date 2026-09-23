import { FactTable, type Fact } from "../../../../components/shared/fact-table";
import { errorMessage } from "../../../../lib/api/error-message";
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
    <section className="mm-quiet-section">
      <details className="mm-log-diagnostics">
        <summary className="mm-quiet-section__head mm-log-diagnostics__summary">
          <span
            id="suite-settings-diagnostics-heading"
            className="mm-quiet-section__title"
          >
            Server diagnostics
          </span>
          <span className="mm-quiet-section__aside">
            <span className="mm-quiet-link mm-log-diagnostics__show">
              Show →
            </span>
            <span className="mm-quiet-link mm-log-diagnostics__hide">
              Hide →
            </span>
          </span>
        </summary>
        <div className="mm-quiet-section__body">
          <p className="mm-quiet-note">
            Advanced counters for troubleshooting. Request issues usually mean a
            browser or API request was rejected or asked for something that was
            not found; they are not the same as application failures.
          </p>
          {metricsQ.isError ? (
            <p className="mt-4 text-sm text-mm-status-failed-text" role="alert">
              {errorMessage(
                metricsQ.error,
                "Could not load server diagnostics.",
              )}
            </p>
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
    </section>
  );
}
