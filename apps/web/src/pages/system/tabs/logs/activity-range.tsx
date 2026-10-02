import type { ActivityLogFilters } from "../../../../lib/activity/activity-filters";

/** The two dates a reader picks for themselves, above the events, once they have chosen to pick dates. */
export function ActivityRange({
  from,
  to,
  onChange,
}: {
  from: string;
  to: string;
  onChange: (next: Partial<ActivityLogFilters>) => void;
}) {
  return (
    <div className="mm-activity-range">
      <label className="mm-field">
        <span className="mm-field__label">From</span>
        <input
          type="datetime-local"
          className="mm-input"
          value={from}
          onChange={(e) => onChange({ from: e.target.value })}
        />
      </label>
      <label className="mm-field">
        <span className="mm-field__label">To</span>
        <input
          type="datetime-local"
          className="mm-input"
          value={to}
          onChange={(e) => onChange({ to: e.target.value })}
        />
      </label>
    </div>
  );
}
