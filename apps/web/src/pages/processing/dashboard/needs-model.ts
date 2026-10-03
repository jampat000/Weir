/** What needs a person, as groups of rows: each says what is wrong, why, and where to go or what to do about it. */
import type { ProcessingFile } from "../../../lib/processing/files-api";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import type { SystemReadiness } from "../../../lib/api/types";
import { plural } from "../../../lib/ui/mm-plural";
import type { StatusMeaning } from "../../../lib/ui/status-meaning";
import { shownBy, type Filter } from "../processing-filter";
import { firstSentence, prettyName } from "../processing-model";
import { fileReason, type FileReason } from "../file-reason";
import {
  activityGroupOf,
  waitsOnAPerson,
  type ActivityGroup,
} from "../../activity/activity-entries";
import { setupTabPath } from "../../../lib/settings/setup-areas";

/** Past this many failed jobs the count reads "100+": the list behind the link has the rest. */
export const FAILED_JOBS_LIMIT = 100;
/** How many files a group lists; the rest are counted in one line that leads to Activity. */
export const FILES_SHOWN_PER_GROUP = 4;
/** How many files that may wait on a person are read. */
export const NEEDS_FILES_READ = 200;
/** The statuses that can leave a file waiting on a person. */
export const NEEDS_FILE_STATUSES = [
  "processing_failed",
  "rejected",
  "on_hold",
  "skipped",
] as const;

/** Where the files that need a look are listed: Activity's failed and rejected files. */
export const NEEDS_A_LOOK_PATH = "/activity?show=failed";

export type NeedRow = {
  key: string;
  /** A failure is broken; what is held, turned away or not set up is waiting on a look. */
  meaning: StatusMeaning;
  title: string;
  /** A few words on why: what kind of stop it is. */
  reason: string;
  /** The server's own sentence, for the row's tooltip. */
  detail?: string;
  /** The file a row is about, which has the file's own actions. */
  file?: ProcessingFile;
  /** Where to read more or fix it, for a row that is not a file. */
  link?: { label: string; to: string };
};

export type NeedGroup = {
  key: string;
  /** "3 not in a language you keep". */
  title: string;
  /** The worst meaning of its rows. */
  meaning: StatusMeaning;
  rows: NeedRow[];
  /** How many more there are than the rows listed, so the group can say where the rest are. */
  more: number;
  /** The Activity view that lists the group's files, where the rest are. None for what is wrong with Weir itself. */
  activity: ActivityGroup | null;
  /** The group's rejected files can all be processed again at once. */
  rejected: boolean;
};

function fileRow(file: ProcessingFile): NeedRow {
  return {
    key: `file-${file.id}`,
    meaning: file.status === "processing_failed" ? "broken" : "attention",
    title: prettyName(file.relative_path),
    reason: fileReason(file).short,
    detail: firstSentence(file.status_reason) || undefined,
    file,
  };
}

/** The order groups are listed in: what went wrong with Weir's work, then what is held, then what was turned away. */
const GROUP_ORDER = [
  "failed-writing",
  "failed-checks",
  "failed-guardrail",
  "failed",
  "stuck",
  "rejected-language",
  "rejected-rules",
  "rejected-replacement",
  "skipped-by-rule",
];

/** Files that wait on a person and share a reason, grouped and put in {@link GROUP_ORDER}. */
function fileGroups(files: readonly ProcessingFile[]): NeedGroup[] {
  const groups = new Map<
    string,
    { reason: FileReason; files: ProcessingFile[] }
  >();
  for (const file of files) {
    const reason = fileReason(file);
    const group = groups.get(reason.key) ?? { reason, files: [] };
    group.files.push(file);
    groups.set(reason.key, group);
  }
  const ordered = [...groups.values()].sort(
    (a, b) =>
      GROUP_ORDER.indexOf(a.reason.key) - GROUP_ORDER.indexOf(b.reason.key),
  );
  return ordered.map(({ reason, files }) => ({
    key: reason.key,
    title: `${files.length.toLocaleString()} ${reason.words}`,
    meaning: fileRow(files[0]).meaning,
    rows: files.slice(0, FILES_SHOWN_PER_GROUP).map(fileRow),
    more: Math.max(0, files.length - FILES_SHOWN_PER_GROUP),
    activity: activityGroupOf(files[0]),
    rejected: reason.rejected,
  }));
}

function setupNeed(
  libraries: readonly ProcessingLibrary[] | undefined,
): NeedRow | null {
  const watching = libraries?.some(
    (library) => library.enabled && library.watched_folder.trim(),
  );
  if (!libraries || watching) return null;
  return {
    key: "setup",
    meaning: "attention",
    title: "Nothing to watch yet",
    reason: "Add or switch on a workflow",
    detail:
      "Weir picks files up from a workflow's watched folder. Add one, or turn an existing workflow on.",
    link: { label: "Set up a workflow", to: setupTabPath("workflows") },
  };
}

function workerNeeds(
  readiness: Pick<SystemReadiness, "worker_health"> | undefined,
): NeedRow[] {
  return (readiness?.worker_health ?? [])
    .filter((worker) => worker.status === "degraded")
    .map((worker) => ({
      key: `worker-${worker.module}`,
      meaning: "broken" as const,
      title: "Background work has stopped",
      reason: "Not responding · restart Weir",
      detail: worker.detail,
      link: { label: "Open jobs", to: "/system?tab=logs&source=job" },
    }));
}

/** The failed jobs, and whether the list they were counted from was full. */
export type FailedJobs = {
  count: number;
  /** The server returns at most {@link FAILED_JOBS_LIMIT} jobs, so there may be more than were counted. */
  capped: boolean;
};

/** Every failed job counts, of any kind of work: a job that fails is Weir's own trouble, not one workflow's. */
export function failedJobsOf(jobs: readonly unknown[]): FailedJobs {
  return { count: jobs.length, capped: jobs.length >= FAILED_JOBS_LIMIT };
}

function failedJobsNeed({ count, capped }: FailedJobs): NeedRow | null {
  if (count === 0) return null;
  const shown = capped ? `${count}+` : `${count}`;
  return {
    key: "failed-jobs",
    meaning: "broken",
    title: count === 1 ? "1 job failed" : `${shown} jobs failed`,
    reason: "Each says what to do next",
    link: {
      label: "Review failed jobs",
      to: "/system?tab=logs&source=job&status=failed",
    },
  };
}

/** The key of the group that holds what is wrong with Weir itself rather than with any file. */
const WEIR_GROUP_KEY = "weir";

/** What is wrong with Weir itself rather than with a file: no workflow, stopped work, failed jobs. */
function weirGroup(rows: NeedRow[]): NeedGroup[] {
  if (rows.length === 0) return [];
  return [
    {
      key: WEIR_GROUP_KEY,
      title: plural(
        rows.length,
        "thing to fix in Weir",
        "things to fix in Weir",
      ),
      meaning: rows.some((row) => row.meaning === "broken")
        ? "broken"
        : "attention",
      rows,
      more: 0,
      activity: null,
      rejected: false,
    },
  ];
}

type NeedSources = {
  workflows: readonly ProcessingLibrary[] | undefined;
  /** Narrows the files to one workflow's. What is wrong with Weir itself is not any one workflow's, and always shows. */
  workflowId: number | null | undefined;
  readiness: Pick<SystemReadiness, "worker_health"> | undefined;
  failedJobs: FailedJobs;
  /** The kind of work the page is narrowed to, which narrows the files. What is wrong with Weir itself always shows. */
  filter: Filter;
  /** The failed, rejected, held and skipped files; only those that wait on a person are listed. */
  files: readonly ProcessingFile[];
};

/**
 * The files that wait on a person, for the workflow and kind of work chosen. Every file in the list came in as a
 * download: a library's files are cleaned from the job queue, with no file row.
 */
export function waitingFiles(
  files: readonly ProcessingFile[],
  workflowId: number | null | undefined,
  filter: Filter,
): ProcessingFile[] {
  if (!shownBy(filter, { source: "download" })) return [];
  return files.filter(
    (file) =>
      waitsOnAPerson(file) &&
      (workflowId == null || file.library_id === workflowId),
  );
}

/** Everything that needs a person, most urgent first: Weir itself, then failed files, then the rest. */
export function buildNeeds({
  workflows,
  workflowId,
  readiness,
  failedJobs,
  filter,
  files,
}: NeedSources): NeedGroup[] {
  const weirRows = [
    setupNeed(workflows),
    ...workerNeeds(readiness),
    failedJobsNeed(failedJobs),
  ].filter((row): row is NeedRow => row !== null);
  const fileRows = waitingFiles(files, workflowId, filter);
  return [...weirGroup(weirRows), ...fileGroups(fileRows)];
}

/**
 * What the panel's fitting sees, in the order it lists them: a group's heading, one of its rows, or its line that
 * leads to the files it does not list.
 */
export type NeedUnit =
  { kind: "heading" } | { kind: "row" } | { kind: "rest"; files: number };

/** The groups as the units the panel lists, in its order. */
export function needUnits(groups: readonly NeedGroup[]): NeedUnit[] {
  return groups.flatMap((group): NeedUnit[] => [
    { kind: "heading" },
    ...group.rows.map((): NeedUnit => ({ kind: "row" })),
    ...(group.more > 0 && group.activity
      ? [{ kind: "rest", files: group.more } as const]
      : []),
  ]);
}

/**
 * How many things are left out when only the first `fits` units show: the rows that are hidden, and the files that a
 * hidden group line would have led to.
 */
export function needsLeftOut(units: readonly NeedUnit[], fits: number): number {
  return units
    .slice(Math.max(0, fits))
    .reduce(
      (left, unit) =>
        left +
        (unit.kind === "row" ? 1 : unit.kind === "rest" ? unit.files : 0),
      0,
    );
}
