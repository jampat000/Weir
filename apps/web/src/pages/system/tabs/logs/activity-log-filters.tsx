import { useState, type Dispatch, type SetStateAction } from "react";

import {
  EMPTY_ACTIVITY_FILTERS,
  anyFilterSet,
  extraFilterCount,
  last24HoursRange,
  lastNightRange,
  type ActivityLogFilters as Filters,
} from "../../../../lib/activity/activity-filters";
import {
  ACTIVITY_RESULT_LABELS,
  ACTIVITY_TRIGGER_LABELS,
} from "../../../../lib/activity/activity-runs";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";

function Choice({
  label,
  value,
  anyLabel,
  options,
  onChange,
}: {
  label: string;
  value: string;
  anyLabel: string;
  options: [value: string, label: string][];
  onChange: (value: string) => void;
}) {
  return (
    <label className="mm-filter-field mm-activity-filters__extra">
      {label}
      <select
        className="mm-input"
        value={value}
        onChange={(e) => onChange(e.target.value)}
      >
        <option value="">{anyLabel}</option>
        {options.map(([optionValue, optionLabel]) => (
          <option key={optionValue} value={optionValue}>
            {optionLabel}
          </option>
        ))}
      </select>
    </label>
  );
}

/**
 * The log's filters. They are edited as a draft and applied together, so typing a search does not
 * refetch on every key. Phones show search first; the rest open on request.
 */
export function ActivityLogFiltersSection({
  draft,
  setDraft,
  applied,
  eventOptions,
  onApply,
}: {
  draft: Filters;
  setDraft: Dispatch<SetStateAction<Filters>>;
  applied: Filters;
  eventOptions: { value: string; label: string }[];
  onApply: (next: Filters) => void;
}) {
  const [moreFilters, setMoreFilters] = useState(false);
  const extra = extraFilterCount(applied);
  const set = (field: keyof Filters) => (value: string) =>
    setDraft((prev) => ({ ...prev, [field]: value }));

  return (
    <section
      className="mm-quiet-section mm-activity-filters"
      aria-labelledby="activity-filters-heading"
      data-testid="activity-filters"
      data-expanded={moreFilters}
    >
      <div className="mm-quiet-section__head">
        <h2 id="activity-filters-heading" className="mm-quiet-section__title">
          Filter events
        </h2>
        <div className="mm-quiet-section__aside">
          <button
            type="button"
            className="mm-quiet-link"
            title="Yesterday 18:00 to today 08:00"
            onClick={() => onApply({ ...draft, ...lastNightRange(new Date()) })}
          >
            Last night →
          </button>
          <button
            type="button"
            className="mm-quiet-link"
            onClick={() =>
              onApply({ ...draft, ...last24HoursRange(new Date()) })
            }
          >
            Last 24 hours →
          </button>
          {anyFilterSet(applied) ? (
            <button
              type="button"
              className="mm-quiet-link"
              onClick={() => onApply(EMPTY_ACTIVITY_FILTERS)}
            >
              Clear →
            </button>
          ) : null}
        </div>
      </div>
      <div className="mm-quiet-section__body">
        <div className="mm-activity-filters__grid">
          <label className="mm-filter-field mm-activity-filters__search">
            Search
            <input
              className="mm-input"
              type="search"
              value={draft.search}
              onChange={(e) => set("search")(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") onApply(draft);
              }}
              placeholder="Search titles and details"
            />
          </label>
          <Choice
            label="Event"
            value={draft.eventType}
            anyLabel="All events"
            options={eventOptions.map((o) => [o.value, o.label])}
            onChange={set("eventType")}
          />
          <Choice
            label="Result"
            value={draft.result}
            anyLabel="Any result"
            options={Object.entries(ACTIVITY_RESULT_LABELS)}
            onChange={set("result")}
          />
          <Choice
            label="Why it happened"
            value={draft.trigger}
            anyLabel="Any reason"
            options={Object.entries(ACTIVITY_TRIGGER_LABELS)}
            onChange={set("trigger")}
          />
          <label className="mm-filter-field mm-activity-filters__extra">
            From
            <input
              type="datetime-local"
              className="mm-input"
              value={draft.from}
              onChange={(e) => set("from")(e.target.value)}
            />
          </label>
          <label className="mm-filter-field mm-activity-filters__extra">
            To
            <input
              type="datetime-local"
              className="mm-input"
              value={draft.to}
              onChange={(e) => set("to")(e.target.value)}
            />
          </label>
        </div>
        <div className="mm-activity-filters__actions">
          <button
            type="button"
            className={mmActionButtonClass({ variant: "primary" })}
            onClick={() => onApply(draft)}
          >
            Apply filters
          </button>
          <button
            type="button"
            className="mm-quiet-link mm-activity-filters__toggle"
            aria-expanded={moreFilters}
            onClick={() => setMoreFilters((open) => !open)}
          >
            {moreFilters
              ? "Fewer filters"
              : extra > 0
                ? `More filters (${extra})`
                : "More filters"}
          </button>
        </div>
      </div>
    </section>
  );
}
