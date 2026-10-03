import type { LogFilters } from "./log-filters";

/** The two times a reader picks for themselves, above the log, once they have chosen a custom range. Read in Weir's time zone. */
export function LogRange({
  from,
  to,
  onChange,
}: {
  from: string;
  to: string;
  onChange: (next: Partial<LogFilters>) => void;
}) {
  return (
    <div className="mm-activity-range" data-testid="logs-range">
      <label className="mm-field">
        <span className="mm-field__label">From</span>
        <input
          type="datetime-local"
          className="mm-input"
          value={from}
          onChange={(event) => onChange({ from: event.target.value })}
        />
      </label>
      <label className="mm-field">
        <span className="mm-field__label">To</span>
        <input
          type="datetime-local"
          className="mm-input"
          value={to}
          onChange={(event) => onChange({ to: event.target.value })}
        />
      </label>
    </div>
  );
}
