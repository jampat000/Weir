import { useState } from "react";
import { Link } from "react-router-dom";

import { Panel } from "../../../components/panels/panel";
import { errorMessage } from "../../../lib/api/error-message";
import type { ProcessingFile } from "../../../lib/processing/files-api";
import { useRequeueProcessingFile } from "../../../lib/processing/files-queries";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import { useProcessingJobsInspectionQuery } from "../../../lib/processing/jobs-inspection/queries";
import { useSystemReadinessQuery } from "../../../lib/system/readiness-queries";
import { ProcessRejectedAgain } from "../../history/history-rejected-again";
import {
  FAILED_JOBS_LIMIT,
  NEEDS_A_LOOK_PATH,
  buildNeeds,
  type Need,
} from "./needs-model";

const NOTHING_NEEDS_YOU = "Nothing needs you right now.";

/** Queues one stuck file again, and says what happened: pending, queued, or why it could not be. */
function RetryFile({ file }: { file: ProcessingFile }) {
  const requeue = useRequeueProcessingFile();
  const [notice, setNotice] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const retry = () => {
    setNotice(null);
    setFailed(false);
    requeue.mutate(file.id, {
      onSuccess: (result) => setNotice(result.detail),
      onError: (error) => {
        setFailed(true);
        setNotice(errorMessage(error, "That file could not be queued again."));
      },
    });
  };
  return (
    <>
      <button
        type="button"
        className="mm-need__button"
        title="Tries this file again now, ignoring the automatic wait and attempt limit."
        disabled={requeue.isPending}
        onClick={retry}
      >
        {requeue.isPending ? "Queueing…" : "Try again"}
      </button>
      {notice ? (
        <span className="mm-need__notice" role={failed ? "alert" : "status"}>
          {notice}
        </span>
      ) : null}
    </>
  );
}

function NeedRow({ need }: { need: Need }) {
  return (
    <li className="mm-need">
      <span className="mm-need__icon" aria-hidden="true">
        !
      </span>
      <div className="mm-need__text">
        <b className="mm-need__title">{need.title}</b>
        <span className="mm-need__reason">{need.reason}</span>
        <span className="mm-need__actions">
          {need.retry ? <RetryFile file={need.retry} /> : null}
          {need.rejectedFiles ? (
            <ProcessRejectedAgain
              libraryId={undefined}
              libraryName={undefined}
            />
          ) : null}
          <Link className="mm-need__link" to={need.link.to}>
            {need.link.label} →
          </Link>
        </span>
      </div>
    </li>
  );
}

type NeedsPanelProps = {
  workflows: readonly ProcessingLibrary[] | undefined;
  /** The files that failed, from the page's own list. */
  stuck: readonly ProcessingFile[];
  /** How many files the rules rejected, from the files list's per-status counts. */
  rejectedCount: number;
};

/** The files and conditions that wait on a person, each with why and what to do. Quiet when there are none. */
export function NeedsPanel({
  workflows,
  stuck,
  rejectedCount,
}: NeedsPanelProps) {
  const readiness = useSystemReadinessQuery();
  // Leaves out a file the owner has since removed from History: this panel is about current problems, not a
  // record of every failure Weir has ever seen (System › Jobs keeps that).
  const failedJobs = useProcessingJobsInspectionQuery(
    "failed",
    FAILED_JOBS_LIMIT,
    true,
  );
  const needs = buildNeeds({
    workflows,
    readiness: readiness.data,
    failedJobCount: failedJobs.data?.jobs.length ?? 0,
    stuck,
    rejectedCount,
  });
  return (
    <Panel
      title="Needs you"
      count={
        needs.length === 0
          ? "nothing right now"
          : `${needs.length.toLocaleString()} to look at`
      }
      to={NEEDS_A_LOOK_PATH}
      toLabel="History"
    >
      {needs.length === 0 ? (
        <p className="mm-needs__calm">{NOTHING_NEEDS_YOU}</p>
      ) : (
        <ul className="mm-needs__list" data-testid="live-needs">
          {needs.map((need) => (
            <NeedRow key={need.key} need={need} />
          ))}
        </ul>
      )}
    </Panel>
  );
}
