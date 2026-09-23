import { Link } from "react-router-dom";

import type { ProcessingFile } from "../../lib/processing/files-api";
import { useProcessingJobsInspectionQuery } from "../../lib/processing/jobs-inspection/queries";
import type { ProcessingLibrary } from "../../lib/processing/libraries-api";
import { useProcessingLibrariesQuery } from "../../lib/processing/libraries-queries";
import { useSystemReadinessQuery } from "../../lib/system/readiness-queries";
import { prettyName } from "./processing-model";

/** Past this many failed jobs the count reads "100+": the list behind the link has the rest. */
export const FAILED_JOBS_LIMIT = 100;

type Need = { key: string; text: string; to: string; action: string };

function setupNeed(libraries: ProcessingLibrary[] | undefined): Need | null {
  const watching = libraries?.some(
    (library) => library.enabled && library.watched_folder.trim(),
  );
  if (!libraries || watching) return null;
  return {
    key: "setup",
    text: "Nothing to watch yet. Weir picks files up from a library's watched folder — add one, or turn an existing library on.",
    to: "/settings?tab=libraries",
    action: "Set up a library",
  };
}

function failedJobsNeed(count: number): Need | null {
  if (count === 0) return null;
  const jobs =
    count === 1
      ? "1 job failed"
      : `${count >= FAILED_JOBS_LIMIT ? `${FAILED_JOBS_LIMIT}+` : count} jobs failed`;
  return {
    key: "failed-jobs",
    text: `${jobs}. Each one says what went wrong and what to do next.`,
    to: "/system?tab=history&show=jobs&status=failed",
    action: "Review failed jobs",
  };
}

function stuckNeed(stuck: ProcessingFile[]): Need | null {
  if (stuck.length === 0) return null;
  return {
    key: "stuck",
    text:
      stuck.length === 1
        ? `${prettyName(stuck[0].relative_path)} is stuck, so your media manager is still missing it. The original is untouched.`
        : `${stuck.length} files are stuck, so your media manager is still missing them. The originals are untouched.`,
    to: "/system?tab=history&show=downloads&status=processing_failed",
    action: "Deal with them",
  };
}

/** What needs a person, in the words the rest of the app uses for the same conditions (#459). */
export function NeedsList({ stuck }: { stuck: ProcessingFile[] }) {
  const libraries = useProcessingLibrariesQuery();
  const readiness = useSystemReadinessQuery();
  const failedJobs = useProcessingJobsInspectionQuery(
    "failed",
    FAILED_JOBS_LIMIT,
  );
  const workers: Need[] = (readiness.data?.worker_health ?? [])
    .filter((worker) => worker.status === "degraded")
    .map((worker) => ({
      key: `worker-${worker.module}`,
      text: `Background work has stopped. ${worker.detail}`,
      to: "/system?tab=history&show=jobs",
      action: "Open jobs",
    }));
  const needs = [
    setupNeed(libraries.data),
    ...workers,
    failedJobsNeed(failedJobs.data?.jobs.length ?? 0),
    stuckNeed(stuck),
  ].filter((need): need is Need => need !== null);

  if (needs.length === 0) return null;
  return (
    <ul className="mm-live-needs" data-testid="live-needs">
      {needs.map((item) => (
        <li key={item.key} className="mm-live-needs__item">
          <span className="mm-live-needs__bang" aria-hidden="true">
            !
          </span>
          <span className="mm-live-needs__text">{item.text}</span>
          <Link className="mm-live-needs__link" to={item.to}>
            {item.action} →
          </Link>
        </li>
      ))}
    </ul>
  );
}
