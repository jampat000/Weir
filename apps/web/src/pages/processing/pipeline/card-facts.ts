/**
 * Every fact a card holds, as a plain sentence each. A card's detail lines show as many of them as its height
 * holds; these are the whole of it, for the card's tooltip and its accessible name, so nothing a file's card knows
 * is left to the size of the window.
 */
import { baseName } from "../../../lib/format/path";
import {
  arrivingDeadline,
  secondsLeft,
  type ArrivingItem,
  type WorkingItem,
  type WorkSource,
} from "../processing-model";
import {
  clock,
  ordinal,
  removedTrackWords,
  ringState,
  workingFigures,
} from "../processing-words";

export const SOURCE_LABEL: Record<WorkSource, string> = {
  download: "Download",
  library: "Library clean",
};

/** "Download · Movies": where the file came from and which workflow has it. */
export function sourceFact(source: WorkSource, workflow: string): string {
  return `${SOURCE_LABEL[source]} · ${workflow}`;
}

/** The facts that say which file and which work a card is: the source and workflow, then the file's own name. */
export function identityFacts(
  source: WorkSource,
  workflow: string,
  path: string,
): string[] {
  return [sourceFact(source, workflow), baseName(path)].filter(Boolean);
}

/** "1st in line". */
export function placeFact(place: number): string {
  return `${ordinal(place - 1)} in line`;
}

/** What an arriving file is waiting for, and when Weir looks at it next, as one sentence. */
export function arrivingNote(item: ArrivingItem, now: number): string {
  const left = secondsLeft(arrivingDeadline(item), now);
  const waitsOnWeir = item.holdUntil == null && item.nextLook != null;
  if (ringState(left) === "checking") {
    if (item.upstream) return `${item.note} Weir is checking again now.`;
    if (waitsOnWeir) return `${item.note} Weir is looking again now.`;
    return "Its wait is over. Weir is checking it now.";
  }
  return waitsOnWeir && left != null
    ? `${item.note} Weir looks again in ${clock(left)}.`
    : item.note;
}

/** What a running pass reports, one fact each: speed, reading rate, how far through, how long, what it removes. */
export function workingFacts(item: WorkingItem): string[] {
  const figures = workingFigures(item);
  const removed = removedTrackWords(item);
  return [
    figures.speed ? `Speed ${figures.speed} real time` : "",
    figures.reading ? `Reading ${figures.reading}` : "",
    figures.through ? `Through the file ${figures.through}` : "",
    figures.running ? `Running for ${figures.running}` : "",
    removed.length > 0 ? `Removing ${removed.join(", ")}` : "",
  ].filter(Boolean);
}
