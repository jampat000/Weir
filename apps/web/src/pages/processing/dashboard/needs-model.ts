/** What needs a person, as groups of rows: each says what is wrong, why, and where to go or what to do about it. */
import {
  REJECTED_BY_RULES,
  type ProcessingFile,
} from "../../../lib/processing/files-api";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import type { SystemReadiness } from "../../../lib/api/types";
import { plural } from "../../../lib/ui/mm-plural";
import { firstSentence, prettyName } from "../processing-model";

/** Past this many failed jobs the count reads "100+": the list behind the link has the rest. */
export const FAILED_JOBS_LIMIT = 100;
/** How many files a group lists; the rest are counted in one line that leads to History. */
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

/** Where the files that need a look are listed: History's failed and rejected files. */
export const NEEDS_A_LOOK_PATH = "/history?show=failed";
const NO_REASON = "Weir could not finish this file. The original is untouched.";

export type NeedRow = {
  key: string;
  title: string;
  reason: string;
  /** The file a row is about, which has the file's own actions. */
  file?: ProcessingFile;
  /** Where to read more or fix it, for a row that is not a file. */
  link?: { label: string; to: string };
};

export type NeedGroup = {
  key: string;
  /** "3 not in a language you keep". */
  title: string;
  rows: NeedRow[];
  /** How many more there are than the rows listed, so the group can say where the rest are. */
  more: number;
  /** The group's rejected files can all be processed again at once. */
  rejected: boolean;
};

type FileReason = { key: string; words: string; rejected: boolean };

/**
 * How the server words a skip for one of the workflow's own rules: its path, size or dates. Any other skip is
 * Weir deciding a file is not for it, which needs nobody.
 */
const SKIPPED_BY_RULE = /^skipped because/i;

/** Whether a file waits on a person: a failure, a rejection, a hold with no clock on it, or a skip by a rule. */
export function waitsOnAPerson(file: ProcessingFile): boolean {
  switch (file.status) {
    case "processing_failed":
    case "rejected":
      return true;
    case "on_hold":
      return !file.hold_until;
    case "skipped":
      return SKIPPED_BY_RULE.test(file.status_reason);
    default:
      return false;
  }
}

/** What the rules say when the audio left nothing in a language the workflow keeps. */
const LANGUAGE_REJECTION = /language|audio tracks/i;

/** What went wrong with a file, in the words a group is titled with. */
function fileReason(file: ProcessingFile): FileReason {
  if (file.status === "rejected") {
    if (LANGUAGE_REJECTION.test(file.status_reason)) {
      return {
        key: "rejected-language",
        words: "not in a language you keep",
        rejected: true,
      };
    }
    return file.failure_class === REJECTED_BY_RULES
      ? {
          key: "rejected-rules",
          words: "turned down by your rules",
          rejected: true,
        }
      : {
          key: "rejected-replacement",
          words: "rejected for a replacement",
          rejected: true,
        };
  }
  if (file.status === "on_hold") {
    return { key: "stuck", words: "stuck", rejected: false };
  }
  if (file.status === "skipped") {
    return {
      key: "skipped-by-rule",
      words: "skipped by a workflow rule",
      rejected: false,
    };
  }
  switch (file.failure_class) {
    case "execution":
      return {
        key: "failed-writing",
        words: "failed while writing",
        rejected: false,
      };
    case "preflight":
      return {
        key: "failed-checks",
        words: "did not pass the checks",
        rejected: false,
      };
    case "guardrail":
      return {
        key: "failed-guardrail",
        words: "stopped to keep the original safe",
        rejected: false,
      };
    default:
      return { key: "failed", words: "failed", rejected: false };
  }
}

function fileRow(file: ProcessingFile): NeedRow {
  return {
    key: `file-${file.id}`,
    title: prettyName(file.relative_path),
    reason: firstSentence(file.status_reason) || NO_REASON,
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

/** Files that share a reason, grouped and put in {@link GROUP_ORDER}. */
function fileGroups(files: readonly ProcessingFile[]): NeedGroup[] {
  const groups = new Map<
    string,
    { reason: FileReason; files: ProcessingFile[] }
  >();
  for (const file of files.filter(waitsOnAPerson)) {
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
    rows: files.slice(0, FILES_SHOWN_PER_GROUP).map(fileRow),
    more: Math.max(0, files.length - FILES_SHOWN_PER_GROUP),
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
    title: "Nothing to watch yet",
    reason:
      "Weir picks files up from a workflow's watched folder. Add one, or turn an existing workflow on.",
    link: { label: "Set up a workflow", to: "/settings?tab=libraries" },
  };
}

function workerNeeds(
  readiness: Pick<SystemReadiness, "worker_health"> | undefined,
): NeedRow[] {
  return (readiness?.worker_health ?? [])
    .filter((worker) => worker.status === "degraded")
    .map((worker) => ({
      key: `worker-${worker.module}`,
      title: "Background work has stopped",
      reason: worker.detail,
      link: { label: "Open jobs", to: "/system?tab=history&show=jobs" },
    }));
}

function failedJobsNeed(count: number): NeedRow | null {
  if (count === 0) return null;
  const shown =
    count >= FAILED_JOBS_LIMIT ? `${FAILED_JOBS_LIMIT}+` : `${count}`;
  return {
    key: "failed-jobs",
    title: count === 1 ? "1 job failed" : `${shown} jobs failed`,
    reason: "Each one says what went wrong and what to do next.",
    link: {
      label: "Review failed jobs",
      to: "/system?tab=history&show=jobs&status=failed",
    },
  };
}

/** What is wrong with Weir itself rather than with a file: no workflow, stopped work, failed jobs. */
function weirGroup(rows: NeedRow[]): NeedGroup[] {
  if (rows.length === 0) return [];
  return [
    {
      key: "weir",
      title: plural(
        rows.length,
        "thing to fix in Weir",
        "things to fix in Weir",
      ),
      rows,
      more: 0,
      rejected: false,
    },
  ];
}

type NeedSources = {
  workflows: readonly ProcessingLibrary[] | undefined;
  /** Narrows the groups to one workflow's files. What is wrong with Weir itself is not any one workflow's. */
  workflowId: number | null | undefined;
  readiness: Pick<SystemReadiness, "worker_health"> | undefined;
  failedJobCount: number;
  /** The failed, rejected, held and skipped files; only those that wait on a person are listed. */
  files: readonly ProcessingFile[];
};

/** Everything that needs a person, most urgent first: Weir itself, then failed files, then the rest. */
export function buildNeeds({
  workflows,
  workflowId,
  readiness,
  failedJobCount,
  files,
}: NeedSources): NeedGroup[] {
  const inWorkflow = (file: ProcessingFile) =>
    workflowId == null || file.library_id === workflowId;
  const weirRows =
    workflowId == null
      ? [
          setupNeed(workflows),
          ...workerNeeds(readiness),
          failedJobsNeed(failedJobCount),
        ].filter((row): row is NeedRow => row !== null)
      : [];
  return [...weirGroup(weirRows), ...fileGroups(files.filter(inWorkflow))];
}

/** How many things need a person: each file and each problem with Weir counts once. */
export function needCount(groups: readonly NeedGroup[]): number {
  return groups.reduce((sum, group) => sum + group.rows.length + group.more, 0);
}
