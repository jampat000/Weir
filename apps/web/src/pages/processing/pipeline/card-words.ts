/**
 * What each card on the Pipeline says, station by station. Every line comes from a reading the server
 * gave (a hold's own clock, the pass's progress, the probe of the file); a line with no real value is left
 * out rather than guessed, and a detail line never says what the status line already says.
 */
import { baseName } from "../../../lib/format/path";
import {
  arrivingDeadline,
  secondsLeft,
  type ArrivingItem,
  type HandingItem,
  type WaitingItem,
  type WorkingItem,
  type WorkSource,
} from "../processing-model";
import {
  clock,
  removedTrackWords,
  ringFraction,
  ringState,
  timeLeft,
  workingFigures,
} from "../processing-words";
import { holdWords, outOfScheduleWords } from "../reason-words";
import type { FlowStepId } from "../stage-flow-model";
import type {
  CardBar,
  CardStatus,
  CardWords,
  DetailLine,
  DetailPart,
} from "./pipeline-card-types";
import type { WorkingStage } from "./pipeline-stages";

const DEFAULT_WORKFLOW = "Workflow";

const SOURCE_LABEL: Record<WorkSource, string> = {
  download: "Download",
  library: "Library clean",
};

/** A bar that only says the pass is moving: no figure says how far it has got. */
const MOVING_BAR: CardBar = { width: 35, waiting: true, moving: true };

const bold = (text: string): DetailPart => ({ bold: text });

/** "Download · Movies": where the file came from and which workflow has it. */
function sourceLine(source: WorkSource, workflow: string): DetailLine {
  return { parts: [bold(SOURCE_LABEL[source]), ` · ${workflow}`] };
}

/** The file's own name, in the monospace line at the foot of a card. */
function fileLine(path: string): DetailLine | null {
  const name = baseName(path);
  return name ? { parts: [name], mono: true } : null;
}

/** The facts the last probe measured, with a quiet remark at the right. Nothing when both are empty. */
function factsLine(facts: string, right?: string): DetailLine | null {
  if (!facts && !right) return null;
  return { parts: facts ? [bold(facts)] : [], right };
}

function present(lines: ReadonlyArray<DetailLine | null>): DetailLine[] {
  return lines.filter((line): line is DetailLine => line !== null);
}

function footer(
  source: WorkSource,
  workflow: string,
  path: string,
): DetailLine[] {
  return present([fileLine(path), sourceLine(source, workflow)]);
}

/** The workflow that has an arriving file: its own name for itself, or the plain word when none is known. */
export function arrivingWorkflow(item: ArrivingItem): string {
  return item.file.library_name || DEFAULT_WORKFLOW;
}

export function incomingWords(item: ArrivingItem, now: number): CardWords {
  const workflow = arrivingWorkflow(item);
  const left = secondsLeft(arrivingDeadline(item), now);
  const checking = ringState(left) === "checking";
  const fraction = ringFraction(item, left);
  const status: CardStatus = checking
    ? { text: "Checking now", tone: "info", pulse: true }
    : {
        text: waitingWords(item),
        tone: "info",
        pulse: false,
        full: item.note || undefined,
      };
  const timing =
    ringState(left) === "counting" && left != null
      ? `${item.holdUntil != null ? "ready in" : "looks again in"} ${clock(left)}`
      : undefined;
  return {
    status,
    bar:
      fraction == null
        ? null
        : { width: Math.round(fraction * 100), waiting: false, moving: false },
    details: present([
      factsLine(item.facts, timing),
      ...footer("download", workflow, item.path),
    ]),
  };
}

/** What an arriving file waits for, in a few words: a media manager still importing it, or the kind of hold it is on. */
function waitingWords(item: ArrivingItem): string {
  if (item.upstream) return "Still importing";
  return item.note ? holdWords(item.note) : "Waiting";
}

export function queuedWords(item: WaitingItem, place: number): CardWords {
  const line = factsLine(
    item.source === "library" ? "" : item.facts,
    `#${place} in line`,
  );
  return {
    status: {
      text: item.note ? outOfScheduleWords(item.note) : "Waiting its turn",
      tone: "idle",
      pulse: false,
      full: item.note ?? undefined,
    },
    bar: null,
    details: present([
      line,
      ...footer(item.source, item.libraryName, item.path),
    ]),
  };
}

const ANALYSING_TEXT: Partial<Record<FlowStepId, string>> = {
  checking: "Checking file",
  plan: "Planning",
};

function analysingWords(item: WorkingItem): CardWords {
  const running = workingFigures(item).running;
  return {
    status: {
      text: ANALYSING_TEXT[item.step] ?? "Checking file",
      tone: "info",
      pulse: true,
    },
    bar: MOVING_BAR,
    details: present([
      factsLine(item.facts, running ? `running ${running}` : undefined),
      ...footer(item.source, item.libraryName, item.path),
    ]),
  };
}

function writingWords(item: WorkingItem, percent: number): CardWords {
  const figures = workingFigures(item);
  const whole = Math.floor(percent);
  const removed = removedTrackWords(item);
  return {
    status: {
      text: `${whole}% writing`,
      lead: `${whole}%`,
      tone: "info",
      pulse: false,
    },
    bar: { width: whole, waiting: false, moving: true },
    details: present([
      figures.through || item.etaSeconds != null
        ? {
            parts: figures.through ? [bold(figures.through)] : [],
            right: timeLeft(item.etaSeconds) || undefined,
          }
        : null,
      figures.reading || figures.running
        ? {
            parts: figures.reading ? ["Reading ", bold(figures.reading)] : [],
            right: figures.running ? `running ${figures.running}` : undefined,
          }
        : null,
      figures.speed ? { parts: ["Speed ", bold(figures.speed)] } : null,
      removed.length > 0
        ? { parts: ["Removing ", bold(removed.join(", "))] }
        : null,
      ...footer(item.source, item.libraryName, item.path),
    ]),
  };
}

/** What a pass says while it has no percentage to show. */
function unmeasuredProcessingText(item: WorkingItem): string {
  if (item.step === "verify") return "Verifying";
  return item.source === "library" ? "Cleaning" : "Writing copy";
}

function processingWords(item: WorkingItem): CardWords {
  if (
    item.source === "download" &&
    item.step === "write" &&
    item.percent != null
  ) {
    return writingWords(item, item.percent);
  }
  const running = workingFigures(item).running;
  return {
    status: {
      text: unmeasuredProcessingText(item),
      tone: "info",
      pulse: true,
    },
    bar: MOVING_BAR,
    details: present([
      item.source === "download"
        ? factsLine(item.facts, running ? `running ${running}` : undefined)
        : null,
      ...footer(item.source, item.libraryName, item.path),
    ]),
  };
}

export function deliveringWords(item: HandingItem | WorkingItem): CardWords {
  return {
    status: {
      text: item.source === "library" ? "Replacing file" : "Handing back",
      tone: "info",
      pulse: true,
    },
    bar: MOVING_BAR,
    details: footer(item.source, item.libraryName, item.path),
  };
}

/** A pass that is on a step of its stages: Analysing, Processing or Delivering by the step. */
export function workingWords(
  item: WorkingItem,
  stage: WorkingStage,
): CardWords {
  if (stage === "analysing") return analysingWords(item);
  return stage === "processing" ? processingWords(item) : deliveringWords(item);
}

export const DELIVERED_LEAD = "✓ Delivered";

/** A card that has just been delivered: full and green, before it flies to Just finished. */
export function deliveredWords(
  source: WorkSource,
  workflow: string,
  path: string,
): CardWords {
  return {
    status: {
      text: DELIVERED_LEAD,
      lead: DELIVERED_LEAD,
      tone: "ok",
      pulse: false,
    },
    bar: { width: 100, waiting: false, moving: false },
    details: footer(source, workflow, path),
  };
}

/** A card whose file stopped, in the station it stopped at, with a few words on why. A rejection is a decision, so it is not drawn as a failure. */
export function stoppedWords(
  kind: "failed" | "rejected",
  reason: string,
  source: WorkSource,
  workflow: string,
  path: string,
): CardWords {
  return {
    status: {
      text: kind === "failed" ? "Couldn't finish" : "Rejected",
      tone: kind === "failed" ? "bad" : "warn",
      pulse: false,
    },
    bar: null,
    details: present([{ parts: [reason] }, ...footer(source, workflow, path)]),
  };
}
