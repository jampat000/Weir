import { Fragment, useMemo } from "react";

import type { SystemLogRow } from "../../../../lib/system/system-log-api";
import {
  useAppClockSecondsFormatter,
  useAppDateFormatter,
  useAppTimeZone,
} from "../../../../lib/ui/mm-format-date";
import { useNow } from "../../../../lib/ui/use-now";
import { dayHeading, groupByDay } from "./log-days";
import { LogRow, type LogRowActions } from "./log-row";

/** Day headings are worked out again this often, so "Today" becomes "Yesterday" after midnight. */
const HEADING_TICK_MS = 60_000;

/** The rows, newest first, under the day each fell on in Weir's time zone. Each opens to what its source recorded. */
export function LogList({
  rows,
  expandedId,
  onToggle,
  actions,
}: {
  rows: readonly SystemLogRow[];
  expandedId: string | null;
  onToggle: (id: string) => void;
  actions: LogRowActions;
}) {
  const timeZone = useAppTimeZone();
  const clock = useAppClockSecondsFormatter();
  const fullTime = useAppDateFormatter();
  const now = useNow(HEADING_TICK_MS);
  const days = useMemo(() => groupByDay(rows, timeZone), [rows, timeZone]);
  return (
    <ol className="mm-log-list" data-testid="log-feed" aria-label="Log">
      {days.map((day) => (
        <Fragment key={day.key}>
          <li className="mm-log-day" aria-hidden="true">
            {dayHeading(day.key, now, timeZone)}
          </li>
          {day.rows.map((row) => (
            <LogRow
              key={row.id}
              row={row}
              expanded={row.id === expandedId}
              onToggle={() => onToggle(row.id)}
              clock={clock}
              fullTime={(iso) => fullTime(iso)}
              actions={actions}
            />
          ))}
        </Fragment>
      ))}
    </ol>
  );
}
