import type { Lanes } from "../processing-model";
import type { Filter } from "../processing-filter";
import { shownBy } from "./pipeline-cards";

/** "2:28 pm": the clock time the way people read it, the same in every browser. */
export function clockTime(ms: number): string {
  const when = new Date(ms);
  const hours = when.getHours() % 12 || 12;
  const minutes = String(when.getMinutes()).padStart(2, "0");
  return `${hours}:${minutes} ${when.getHours() < 12 ? "am" : "pm"}`;
}

/**
 * When every file on the Pipeline should be through, or undefined when no honest time can be given. The only
 * reading Weir has is a writing pass's own estimate, so a time is given only when every file in progress is
 * one of those: a file still arriving or waiting has no reading, and neither has a pass that has not started
 * writing, so any of them leaves the time out.
 */
export function allDoneBy(
  lanes: Lanes,
  filter: Filter,
  now: number,
): number | undefined {
  const arriving = filter === "library" ? 0 : lanes.arriving.length;
  const waiting = lanes.waiting.filter((item) => shownBy(filter, item)).length;
  const working = lanes.working.filter((item) => shownBy(filter, item));
  if (arriving > 0 || waiting > 0 || working.length === 0) return undefined;
  const remaining = working.map((item) =>
    item.step === "write" ? item.etaSeconds : null,
  );
  if (remaining.some((seconds) => seconds == null)) return undefined;
  return now + Math.max(...remaining.map((seconds) => seconds ?? 0)) * 1000;
}

/** What follows "Pipeline" in the heading: "8 in progress · all done by about 2:28 pm". */
export function pipelineCount(inProgress: number, doneBy?: number): string {
  if (inProgress <= 0) return "nothing in progress right now";
  const base = `${inProgress.toLocaleString()} in progress`;
  return doneBy === undefined
    ? base
    : `${base} · all done by about ${clockTime(doneBy)}`;
}
