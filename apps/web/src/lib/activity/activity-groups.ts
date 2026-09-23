import type { ActivityEventItem } from "../api/types";
import { eventDisplay } from "./activity-display";

export type ActivityGroup = {
  /** failures: repeated failures, listed; repeat: identical routine entries, one row ×N; run: one run's entries. */
  kind: "failures" | "repeat" | "run";
  key: string;
  events: ActivityEventItem[];
};

function groupRepeats(items: ActivityEventItem[]): ActivityGroup[] {
  const groups: ActivityGroup[] = [];
  for (const event of items) {
    const display = eventDisplay(event);
    const isFailure =
      display.tone === "error" || /failed|denied/i.test(event.event_type);
    const previous = groups.at(-1);
    const key = isFailure
      ? `failures|${event.module}|${event.event_type}|${display.title}`
      : // Routine entries only merge when nothing tells them apart (e.g. six sign-ins).
        `repeat|${event.module}|${event.event_type}|${display.title}|${event.detail ?? ""}|${event.trigger ?? ""}|${event.relative_path ?? ""}`;
    if (previous?.key === key) {
      previous.events.push(event);
    } else {
      groups.push({
        kind: isFailure ? "failures" : "repeat",
        key,
        events: [event],
      });
    }
  }
  return groups;
}

/**
 * Consecutive entries that share a run collapse under one summary row; everything else keeps the
 * repeated-entry clustering. A run of one entry is shown as that entry.
 */
export function groupActivityFeed(items: ActivityEventItem[]): ActivityGroup[] {
  const groups: ActivityGroup[] = [];
  let loose: ActivityEventItem[] = [];
  const flushLoose = () => {
    groups.push(...groupRepeats(loose));
    loose = [];
  };
  let index = 0;
  while (index < items.length) {
    const runKey = items[index].run_key;
    let end = index + 1;
    if (runKey) {
      while (end < items.length && items[end].run_key === runKey) end += 1;
    }
    if (runKey && end - index > 1) {
      flushLoose();
      groups.push({
        kind: "run",
        key: `run|${runKey}|${items[index].id}`,
        events: items.slice(index, end),
      });
      index = end;
    } else {
      loose.push(items[index]);
      index += 1;
    }
  }
  flushLoose();
  return groups;
}
