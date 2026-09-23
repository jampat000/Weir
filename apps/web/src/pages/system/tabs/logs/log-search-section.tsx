import { QuietSection } from "../../../../components/shared/quiet-section";
import {
  mmActionButtonClass,
  mmEditableTextFieldClass,
} from "../../../../lib/ui/mm-control-roles";
import {
  LOG_LEVEL_OPTIONS,
  logLevelLabel,
  type LogLevelFilter,
} from "./server-log-format";

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

/** The server log's filters, with a badge for each one that is set. */
export function LogSearchSection({
  search,
  onChange,
  refreshing,
  onRefresh,
}: {
  search: LogSearch;
  onChange: (next: LogSearch) => void;
  refreshing: boolean;
  onRefresh: () => void;
}) {
  const text = search.text.trim();
  const anySet = Boolean(text || search.level || search.tracebacksOnly);
  const setTracebacks = (tracebacksOnly: boolean) =>
    onChange({ ...search, tracebacksOnly });

  return (
    <QuietSection
      level={3}
      headingId="suite-settings-logs-filters-heading"
      heading="Search logs"
      aside={
        <>
          <button
            type="button"
            className="mm-quiet-link"
            disabled={refreshing}
            onClick={onRefresh}
          >
            {refreshing ? "Refreshing…" : "Refresh →"}
          </button>
          <button
            type="button"
            className="mm-quiet-link"
            disabled={!anySet}
            onClick={() => onChange(EMPTY_LOG_SEARCH)}
          >
            Clear filters →
          </button>
        </>
      }
    >
      <p className="mm-quiet-note">
        Search message text, component names, tracebacks, request IDs, and job
        IDs. This view refreshes while it is open.
      </p>

      <div className="mm-log-search">
        <label className="mm-filter-field">
          Search
          <input
            type="text"
            className={mmEditableTextFieldClass}
            placeholder="Search message, detail, traceback, logger, or source"
            value={search.text}
            onChange={(e) => onChange({ ...search, text: e.target.value })}
          />
        </label>
        <label className="mm-filter-field">
          Level
          <select
            className={mmEditableTextFieldClass}
            value={search.level}
            onChange={(e) =>
              onChange({ ...search, level: e.target.value as LogLevelFilter })
            }
          >
            {LOG_LEVEL_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </label>
        <div className="mm-filter-field">
          <span>Tracebacks only</span>
          <div className="flex gap-2">
            <button
              type="button"
              className={mmActionButtonClass({
                variant: search.tracebacksOnly ? "primary" : "tertiary",
              })}
              onClick={() => setTracebacks(true)}
            >
              On
            </button>
            <button
              type="button"
              className={mmActionButtonClass({
                variant: search.tracebacksOnly ? "tertiary" : "primary",
              })}
              onClick={() => setTracebacks(false)}
            >
              Off
            </button>
          </div>
        </div>
      </div>

      {anySet ? (
        <div className="mt-3 flex flex-wrap gap-2">
          {text ? <span className="mm-quiet-badge">Search: {text}</span> : null}
          {search.level ? (
            <span className="mm-quiet-badge">
              Level: {logLevelLabel(search.level)}
            </span>
          ) : null}
          {search.tracebacksOnly ? (
            <span className="mm-quiet-badge">Tracebacks only</span>
          ) : null}
        </div>
      ) : null}
    </QuietSection>
  );
}
