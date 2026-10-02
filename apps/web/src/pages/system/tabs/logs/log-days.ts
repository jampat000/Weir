import type { SystemLogRow } from "../../../../lib/system/system-log-api";
import { parseAppTime } from "../../../../lib/ui/mm-format-date";
import { dayKeyAt, previousDayKeyAt } from "../../../../lib/ui/zoned-time";

/** The rows of one calendar day, newest first, under the words that name the day. */
export type LogDay = {
  /** The day as "2026-10-02", in Weir's time zone. */
  key: string;
  rows: SystemLogRow[];
};

/** Splits newest-first rows into the days they fall on in `timeZone`, keeping the order. A row with no readable time joins the day before it. */
export function groupByDay(
  rows: readonly SystemLogRow[],
  timeZone: string | undefined,
): LogDay[] {
  const days: LogDay[] = [];
  for (const row of rows) {
    const at = parseAppTime(row.at);
    const key = at === null ? (days.at(-1)?.key ?? "") : dayKeyAt(at, timeZone);
    const last = days.at(-1);
    if (last && last.key === key) last.rows.push(row);
    else days.push({ key, rows: [row] });
  }
  return days;
}

const HEADING_DATE: Intl.DateTimeFormatOptions = {
  weekday: "long",
  day: "numeric",
  month: "long",
};

/** What names a day: "Today", "Yesterday", or "Friday 25 September", with the year when it is not this one. */
export function dayHeading(
  key: string,
  now: number,
  timeZone: string | undefined,
): string {
  if (key === dayKeyAt(now, timeZone)) return "Today";
  if (key === previousDayKeyAt(now, timeZone)) return "Yesterday";
  const [year, month, day] = key.split("-").map(Number);
  // Noon UTC is the same calendar day everywhere that matters, so the name below is of the day the key names.
  const noon = Date.UTC(year, month - 1, day, 12);
  const thisYear = Number(dayKeyAt(now, timeZone).slice(0, 4)) === year;
  try {
    return new Intl.DateTimeFormat(undefined, {
      ...HEADING_DATE,
      ...(thisYear ? {} : { year: "numeric" }),
      timeZone: "UTC",
    }).format(noon);
  } catch {
    return key;
  }
}
