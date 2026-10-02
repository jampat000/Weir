/**
 * What Weir will do next on its own, soonest first: each workflow's next look at its watched folder, the
 * daily clean of a library, and the cleanup jobs. Every time comes from the server; a job that is switched
 * off, or has no time, is left out rather than guessed.
 */
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import type { MaintenanceFamilyState } from "../../../lib/processing/maintenance-api";
import { parseAppTime } from "../../../lib/ui/mm-format-date";
import { CLEANUP_JOBS } from "../../settings/tabs/cleanup/cleanup-jobs";
import { shownBy, type Filter } from "../processing-filter";
import { clockTime } from "../pipeline/pipeline-heading";

const SECOND_MS = 1000;
const MINUTE_MS = 60 * SECOND_MS;
export const DAY_SECONDS = 24 * 60 * 60;
/** Under this a wait is counted in seconds; under `MINUTES_UNTIL_MS` in minutes; beyond it, a time of day. */
const SECONDS_UNTIL_MS = 90 * SECOND_MS;
const MINUTES_UNTIL_MS = 90 * MINUTE_MS;

export type NextItem = {
  key: string;
  label: string;
  to: string;
  /** When it happens, in ms since the epoch. */
  at: number;
  /** How long the wait before it is in all, when that is known: the bar fills as the moment nears. */
  intervalSeconds: number | null;
};

/** A workflow's library clean, with the time the server says it next runs. */
export type LibraryCleanRun = {
  libraryId: number;
  libraryName: string;
  nextRunAt: string | null;
};

type NextSources = {
  workflows: readonly ProcessingLibrary[];
  cleanRuns: readonly LibraryCleanRun[];
  cleanupJobs: readonly MaintenanceFamilyState[];
  /** The kind of work to list: a scan finds new downloads, a library's clean is its own. A cleanup job is neither. */
  filter?: Filter;
};

function workflowPath(id: number): string {
  return `/settings?tab=libraries&edit=${id}`;
}

function scans(workflows: readonly ProcessingLibrary[]): NextItem[] {
  return workflows.flatMap((workflow) => {
    const at = parseAppTime(workflow.next_look_at);
    if (!workflow.enabled || !workflow.watched_folder.trim() || at === null) {
      return [];
    }
    return [
      {
        key: `scan-${workflow.id}`,
        label: `Scan ${workflow.name}`,
        to: workflowPath(workflow.id),
        at,
        intervalSeconds: workflow.scan_interval_seconds,
      },
    ];
  });
}

function libraryCleans(runs: readonly LibraryCleanRun[]): NextItem[] {
  return runs.flatMap((run) => {
    const at = parseAppTime(run.nextRunAt);
    if (at === null) return [];
    return [
      {
        key: `clean-${run.libraryId}`,
        label: `Clean ${run.libraryName} library`,
        to: "/library",
        at,
        intervalSeconds: DAY_SECONDS,
      },
    ];
  });
}

function cleanups(jobs: readonly MaintenanceFamilyState[]): NextItem[] {
  return jobs.flatMap((job) => {
    const at = parseAppTime(job.next_run_at);
    const label = CLEANUP_JOBS.find(
      (known) => known.family === job.family,
    )?.timerLabel;
    if (!job.enabled || at === null || !label) return [];
    return [
      {
        key: `cleanup-${job.family}`,
        label,
        to: "/settings?tab=cleanup",
        at,
        intervalSeconds: job.interval_seconds ?? null,
      },
    ];
  });
}

export function nextItems({
  workflows,
  cleanRuns,
  cleanupJobs,
  filter = "all",
}: NextSources): NextItem[] {
  return [
    ...(shownBy(filter, { source: "download" }) ? scans(workflows) : []),
    ...(shownBy(filter, { source: "library" }) ? libraryCleans(cleanRuns) : []),
    ...(filter === "all" ? cleanups(cleanupJobs) : []),
  ].sort((a, b) => a.at - b.at);
}

/** How far through its wait an item is, from 0 to 1; null when the whole wait is not known. */
export function waitFraction(item: NextItem, now: number): number | null {
  if (item.intervalSeconds === null || item.intervalSeconds <= 0) return null;
  const left = Math.max(0, item.at - now);
  return Math.min(
    1,
    Math.max(0, 1 - left / (item.intervalSeconds * SECOND_MS)),
  );
}

function sameDay(a: Date, b: Date): boolean {
  return a.toDateString() === b.toDateString();
}

/** A weekday and a clock time, or "tomorrow", for a moment on another day. */
function dayWords(at: number, now: number): string {
  const tomorrow = new Date(now);
  tomorrow.setDate(tomorrow.getDate() + 1);
  return sameDay(new Date(at), tomorrow)
    ? "tomorrow"
    : new Date(at).toLocaleDateString("en-US", { weekday: "short" });
}

/** The big figure of the tile: a countdown, or a clock time when the moment is further off. */
export function figureWords(at: number, now: number): string {
  const left = at - now;
  if (left <= 0) return "now";
  if (left < SECONDS_UNTIL_MS) return `${Math.ceil(left / SECOND_MS)} s`;
  if (left < MINUTES_UNTIL_MS) return `${Math.round(left / MINUTE_MS)} min`;
  return sameDay(new Date(at), new Date(now))
    ? clockTime(at)
    : dayWords(at, now);
}

/** The "then" lines: the same, with the day beside a clock time on another day. */
export function lineWords(at: number, now: number): string {
  const left = at - now;
  if (left < MINUTES_UNTIL_MS || sameDay(new Date(at), new Date(now))) {
    return figureWords(at, now);
  }
  return `${dayWords(at, now)} ${clockTime(at)}`;
}
