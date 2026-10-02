import { plural } from "../../../../lib/ui/mm-plural";

/** What the Events card's header says about the list under it, from what the list holds. */
export type EventsStatusInput = {
  /** Events in hand. */
  loaded: number;
  /** Events the filters match, loaded or not. */
  total: number;
  filtered: boolean;
  /** How many days Weir keeps events, or 0 for until they are cleared; undefined before the server has said. */
  retentionDays: number | undefined;
  /** The day of the oldest event, as the reader writes it ("2 Oct"), or null when there is none. */
  oldestDay: string | null;
};

/**
 * One quiet line for the Events card: how many events, that the list is live, and how far back it goes.
 * "3 events · live · back to 2 Oct".
 */
export function eventsStatusWords({
  loaded,
  total,
  filtered,
  retentionDays,
  oldestDay,
}: EventsStatusInput): string {
  const parts = [
    loaded < total
      ? `Showing ${loaded} of ${plural(total, "event", "events")}`
      : filtered
        ? `${plural(total, "event", "events")} matching`
        : plural(total, "event", "events"),
    "live",
  ];
  if (retentionDays === 0) parts.push("kept until cleared");
  else if (oldestDay) parts.push(`back to ${oldestDay}`);
  return parts.join(" · ");
}
