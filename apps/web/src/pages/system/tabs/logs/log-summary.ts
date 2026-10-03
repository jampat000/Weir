import { plural } from "../../../../lib/ui/mm-plural";

/** What the Log card's header says about the list under it, from what the list holds. */
export type LogSummaryInput = {
  /** Rows in hand. */
  loaded: number;
  /** Rows the filters match, loaded or not. */
  total: number;
  filtered: boolean;
};

/**
 * One quiet line for the Log card: how many entries, and that the list is live. "212 entries · live", "Showing 50 of 212
 * entries · live", "3 entries matching · live".
 */
export function logSummaryWords({
  loaded,
  total,
  filtered,
}: LogSummaryInput): string {
  const count = plural(total, "entry", "entries");
  const held =
    loaded < total
      ? `Showing ${loaded.toLocaleString()} of ${count}`
      : filtered
        ? `${count} matching`
        : count;
  return `${held} · live`;
}
