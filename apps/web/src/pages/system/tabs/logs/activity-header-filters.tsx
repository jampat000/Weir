import { HeaderSearch } from "../../../../components/shell/header-search";
import {
  last24HoursRange,
  lastNightRange,
  type ActivityLogFilters as Filters,
} from "../../../../lib/activity/activity-filters";
import {
  ACTIVITY_RESULT_LABELS,
  ACTIVITY_TRIGGER_LABELS,
} from "../../../../lib/activity/activity-runs";
import {
  LogsPicker,
  LogsViewControls,
  useSearchCollapsed,
} from "./logs-header-controls";

/** How far back the log is read: a quick range, or dates of the reader's own, which show above the list. */
export type When = "any" | "last-night" | "last-24-hours" | "custom";

const WHEN_OPTIONS: readonly { value: When; label: string }[] = [
  { value: "any", label: "Any time" },
  { value: "last-night", label: "Last night" },
  { value: "last-24-hours", label: "Last 24 hours" },
  { value: "custom", label: "Pick dates" },
];

/** The dates a quick range stands for, from now; a chosen range of the reader's own has none to give. */
export function rangeFor(when: When, now: Date): Pick<Filters, "from" | "to"> {
  if (when === "last-night") return lastNightRange(now);
  if (when === "last-24-hours") return last24HoursRange(now);
  return { from: "", to: "" };
}

const toOptions = (
  anyLabel: string,
  entries: readonly [value: string, label: string][],
) => [
  { value: "", label: anyLabel },
  ...entries.map(([value, label]) => ({ value, label })),
];

/**
 * The event log's filters, on the header's title line after "Show". Each applies as it is chosen; the search waits for
 * a pause in typing (the page debounces it), as History's does.
 */
export function ActivityHeaderFilters({
  filters,
  search,
  onSearch,
  onChange,
  when,
  onWhen,
  eventOptions,
}: {
  filters: Filters;
  /** What is typed in the search box, before it is applied. */
  search: string;
  onSearch: (next: string) => void;
  onChange: (next: Partial<Filters>) => void;
  when: When;
  onWhen: (next: When) => void;
  eventOptions: { value: string; label: string }[];
}) {
  const collapsed = useSearchCollapsed();
  return (
    <LogsViewControls>
      <HeaderSearch
        label="Search events"
        placeholder="Search events"
        className="mm-history-search-box"
        collapsed={collapsed}
        value={search}
        onChange={(event) => onSearch(event.target.value)}
      />
      <LogsPicker
        label="Event"
        options={toOptions(
          "All events",
          eventOptions.map((o) => [o.value, o.label]),
        )}
        value={filters.eventType}
        onChange={(eventType) => onChange({ eventType })}
      />
      <LogsPicker
        label="Result"
        options={toOptions(
          "Any result",
          Object.entries(ACTIVITY_RESULT_LABELS),
        )}
        value={filters.result}
        onChange={(result) => onChange({ result })}
      />
      <LogsPicker
        label="Why it happened"
        options={toOptions(
          "Any cause",
          Object.entries(ACTIVITY_TRIGGER_LABELS),
        )}
        value={filters.trigger}
        onChange={(trigger) => onChange({ trigger })}
      />
      <LogsPicker
        label="When"
        options={WHEN_OPTIONS}
        value={when}
        onChange={(next) => onWhen(next as When)}
      />
    </LogsViewControls>
  );
}
