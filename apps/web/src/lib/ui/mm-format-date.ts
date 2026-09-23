import { useCallback } from "react";
import { useAppSettingsQuery } from "../settings/queries";

/** A trailing "Z", "+10:00", "-0500": the timestamp already says which zone it is in. */
const HAS_ZONE = /(?:[zZ]|[+-]\d\d:?\d\d)$/;

/** The server writes UTC timestamps without a zone, so one without a zone is read as UTC. */
export function parseAppDate(iso: string): Date {
  return new Date(HAS_ZONE.test(iso) ? iso : `${iso}Z`);
}

/** A server timestamp in ms since the epoch, or null when it is missing or unreadable. */
export function parseAppTime(iso: string | null | undefined): number | null {
  if (!iso) return null;
  const ms = parseAppDate(iso).getTime();
  return Number.isNaN(ms) ? null : ms;
}

/** Formats server timestamps in the timezone chosen in Settings, or the browser's when none is set. */
export function useAppDateFormatter(): (
  iso: string | null | undefined,
) => string {
  const q = useAppSettingsQuery();
  const tz = q.data?.app_timezone || undefined;

  return useCallback(
    (iso: string | null | undefined): string => {
      if (!iso) return "—";
      try {
        return new Intl.DateTimeFormat(undefined, {
          dateStyle: "medium",
          timeStyle: "short",
          timeZone: tz,
        }).format(parseAppDate(iso));
      } catch {
        // An unreadable timestamp or an unknown timezone: show what the server sent.
        return iso;
      }
    },
    [tz],
  );
}
