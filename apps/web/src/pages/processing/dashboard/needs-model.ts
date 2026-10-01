/** What needs a person, as rows: each says what is wrong, why, and where to go or what to do about it. */
import type { ProcessingFile } from "../../../lib/processing/files-api";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import type { SystemReadiness } from "../../../lib/api/types";
import { plural } from "../../../lib/ui/mm-plural";
import { firstSentence, prettyName } from "../processing-model";

/** Past this many failed jobs the count reads "100+": the list behind the link has the rest. */
export const FAILED_JOBS_LIMIT = 100;
/** How many stuck files get a row of their own; the rest are counted in one line that leads to History. */
export const STUCK_FILES_SHOWN = 3;

/** Where the files that need a look are listed: History's failed and rejected files. */
export const NEEDS_A_LOOK_PATH = "/history?show=failed";
const NO_REASON = "Weir could not finish this file. The original is untouched.";

export type Need = {
  key: string;
  title: string;
  reason: string;
  link: { label: string; to: string };
  /** A stuck file Weir can try again from here. */
  retry?: ProcessingFile;
  /** The rejected files, which can all be processed again at once. */
  rejectedFiles?: boolean;
};

function setupNeed(
  libraries: readonly ProcessingLibrary[] | undefined,
): Need | null {
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
): Need[] {
  return (readiness?.worker_health ?? [])
    .filter((worker) => worker.status === "degraded")
    .map((worker) => ({
      key: `worker-${worker.module}`,
      title: "Background work has stopped",
      reason: worker.detail,
      link: { label: "Open jobs", to: "/system?tab=history&show=jobs" },
    }));
}

function failedJobsNeed(count: number): Need | null {
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

function stuckNeeds(stuck: readonly ProcessingFile[]): Need[] {
  const own = stuck.slice(0, STUCK_FILES_SHOWN).map((file) => ({
    key: `stuck-${file.id}`,
    title: prettyName(file.relative_path),
    reason: firstSentence(file.status_reason) || NO_REASON,
    link: {
      label: "Open in History",
      to: `/history?q=${encodeURIComponent(file.relative_path)}`,
    },
    retry: file,
  }));
  const more = stuck.length - own.length;
  if (more <= 0) return own;
  return [
    ...own,
    {
      key: "stuck-more",
      title: `and ${plural(more, "more stuck file", "more stuck files")}`,
      reason:
        "Your media manager is still missing them. The originals are untouched.",
      link: { label: "Open in History", to: NEEDS_A_LOOK_PATH },
    },
  ];
}

function rejectedNeed(count: number): Need | null {
  if (count === 0) return null;
  return {
    key: "rejected",
    title: plural(count, "file was rejected", "files were rejected"),
    reason:
      "Your rules turned them down. After changing the rules, they can all be checked again.",
    link: { label: "Review in History", to: NEEDS_A_LOOK_PATH },
    rejectedFiles: true,
  };
}

type NeedSources = {
  workflows: readonly ProcessingLibrary[] | undefined;
  readiness: Pick<SystemReadiness, "worker_health"> | undefined;
  failedJobCount: number;
  stuck: readonly ProcessingFile[];
  rejectedCount: number;
};

/** Everything that needs a person, most urgent first: setup, stopped work, failed jobs, stuck files, rejected files. */
export function buildNeeds({
  workflows,
  readiness,
  failedJobCount,
  stuck,
  rejectedCount,
}: NeedSources): Need[] {
  return [
    setupNeed(workflows),
    ...workerNeeds(readiness),
    failedJobsNeed(failedJobCount),
    ...stuckNeeds(stuck),
    rejectedNeed(rejectedCount),
  ].filter((need): need is Need => need !== null);
}
