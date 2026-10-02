/** Times as the System cards say them, in the timezone chosen in Settings (the browser's when none is). */

const MINUTE_MS = 60_000;
const HOUR_MS = 60 * MINUTE_MS;
const DAY_MS = 24 * HOUR_MS;
const MINUTES_PER_HOUR = 60;
/** A span under this many hours is counted in hours; longer is counted in days. */
const HOURS_SHOWN_UNTIL = 48;

const DAY_PARTS: Intl.DateTimeFormatOptions = {
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
};

/** A formatter in a timezone, or in the browser's when the timezone is unknown or unset. */
function formatterIn(
  locale: string | undefined,
  options: Intl.DateTimeFormatOptions,
  timeZone: string | undefined,
): Intl.DateTimeFormat {
  try {
    return new Intl.DateTimeFormat(locale, { ...options, timeZone });
  } catch {
    return new Intl.DateTimeFormat(locale, options);
  }
}

/** The calendar day of an instant, "2026-10-02", in a timezone. */
export function dayKey(ms: number, timeZone: string | undefined): string {
  return formatterIn("en-CA", DAY_PARTS, timeZone).format(ms);
}

/** A span of time in one rough unit: "12 min", "5 h", "3 days". At least a minute. */
export function spanWords(ms: number): string {
  const minutes = Math.max(1, Math.round(ms / MINUTE_MS));
  if (minutes < MINUTES_PER_HOUR) return `${minutes} min`;
  if (ms < HOURS_SHOWN_UNTIL * HOUR_MS) {
    return `${Math.round(ms / HOUR_MS)} h`;
  }
  const days = Math.round(ms / DAY_MS);
  return `${days} days`;
}

export type WhenFormat = {
  timeZone: string | undefined;
  /** A clock time, "3:00 am". */
  clock: (ms: number) => string;
};

/** "Today 3:00 am", "Yesterday 3:00 am", or the date and time. */
export function whenWords(at: number, now: number, format: WhenFormat): string {
  const day = dayKey(at, format.timeZone);
  const clock = format.clock(at);
  if (day === dayKey(now, format.timeZone)) return `Today ${clock}`;
  if (day === dayKey(now - DAY_MS, format.timeZone)) {
    return `Yesterday ${clock}`;
  }
  const date = formatterIn(
    undefined,
    { day: "numeric", month: "short" },
    format.timeZone,
  ).format(at);
  return `${date} ${clock}`;
}
