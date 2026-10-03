import type {
  SortValue,
  TableColumnsConfig,
} from "../../../../lib/ui/table-columns";
import type { NotificationChannelOut } from "../../../../lib/settings/types";
import { eventLabel } from "./alert-events";

export const ALERT_NAME_COLUMN = "alert";
export const ALERT_ACTIONS_COLUMN = "actions";

const EVENT_COLUMN_PREFIX = "event:";

/** The column for one event: what a tick in it means is that the alert hears about the event. */
export function eventColumnId(event: string): string {
  return EVENT_COLUMN_PREFIX + event;
}

/**
 * The alerts table: a column for the alert, one for each event the server can send, and the buttons. Which events there
 * are comes from the server, so the columns are made for them. All the alerts are loaded, so the browser sorts them; a
 * column of ticks sorts the alerts that hear of the event first.
 */
export function alertColumns(
  events: readonly string[],
): TableColumnsConfig<string> {
  return {
    tableId: "alert-channels",
    sortable: true,
    defaultSort: null,
    columns: [
      { id: ALERT_NAME_COLUMN, label: "Alert" },
      ...events.map((event) => ({
        id: eventColumnId(event),
        label: eventLabel(event),
        firstDirection: "desc" as const,
      })),
      {
        id: ALERT_ACTIONS_COLUMN,
        label: "Actions",
        movable: false,
        sortable: false,
      },
    ],
  };
}

/** What each column that sorts sorts by: the alert's name, and for an event whether it hears of it. */
export function alertSortValues(
  events: readonly string[],
): Record<string, (channel: NotificationChannelOut) => SortValue> {
  return {
    [ALERT_NAME_COLUMN]: (channel) => channel.label,
    ...Object.fromEntries(
      events.map((event) => [
        eventColumnId(event),
        (channel: NotificationChannelOut) =>
          Number(channel.events.includes(event)),
      ]),
    ),
  };
}
