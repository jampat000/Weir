import {
  eventLabel,
  eventOptions,
} from "../../../../lib/activity/activity-display";
import { useActivityRecentQuery } from "../../../../lib/activity/queries";
import { ActivityEvents } from "./activity-events";
import { ActivityHeaderFilters } from "./activity-header-filters";
import { ActivityRange } from "./activity-range";
import { useActivityFilters } from "./use-activity-filters";

/**
 * Weir's own events (System › Logs): the filters on the header's title line, the list under it. The list is keyed on
 * what it asks the server for, so a change of filter starts it again from the first page.
 */
export function ActivityLog() {
  const filters = useActivityFilters();
  // The same question the list asks, so the answer is shared: the Event choice offers the types it contains.
  const recent = useActivityRecentQuery(filters.query);
  const options = eventOptions(recent.data?.items ?? []);
  const chosenType = filters.chosen.eventType;
  const eventChoices =
    chosenType && !options.some((option) => option.value === chosenType)
      ? [...options, { value: chosenType, label: eventLabel(chosenType) }]
      : options;

  return (
    <>
      <ActivityHeaderFilters
        filters={filters.chosen}
        search={filters.typed}
        onSearch={filters.setTyped}
        onChange={filters.change}
        when={filters.when}
        onWhen={filters.chooseWhen}
        eventOptions={eventChoices}
      />
      <ActivityEvents
        key={JSON.stringify(filters.query)}
        applied={filters.applied}
        queryFilters={filters.query}
        onClearFilters={filters.clear}
        range={
          filters.when === "custom" ? (
            <ActivityRange
              from={filters.chosen.from}
              to={filters.chosen.to}
              onChange={filters.change}
            />
          ) : null
        }
      />
    </>
  );
}
