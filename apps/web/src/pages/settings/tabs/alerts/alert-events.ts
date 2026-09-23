/**
 * What each event means, in the order the columns read. "Anything" covers files and Weir's own jobs alike: a file's
 * pass is a job, and Weir's own jobs (backups, cleanup, scans) only ever alert when they fail for good.
 */
const EVENT_LABELS: Record<string, string> = {
  processing_job_completed: "A file finished",
  processing_job_failed: "A file failed for good",
  job_failed: "Anything failed for good",
  job_completed: "Anything finished",
};

const EVENT_ORDER = Object.keys(EVENT_LABELS);

/** An event in words; one this screen has no words for yet shows as the server names it. */
export function eventLabel(event: string): string {
  return EVENT_LABELS[event] ?? event;
}

/** The server's events, the known ones in reading order and any newer ones after them. */
export function orderedEvents(supported: string[] | undefined): string[] {
  return [
    ...EVENT_ORDER.filter((event) =>
      (supported ?? EVENT_ORDER).includes(event),
    ),
    ...(supported ?? []).filter((event) => !EVENT_ORDER.includes(event)),
  ];
}
