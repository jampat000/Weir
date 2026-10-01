import { useState } from "react";
import { Link } from "react-router-dom";

import { Panel } from "../../../components/panels/panel";
import type { ProcessingFile } from "../../../lib/processing/files-api";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import { useProcessingJobsInspectionQuery } from "../../../lib/processing/jobs-inspection/queries";
import { useSystemReadinessQuery } from "../../../lib/system/readiness-queries";
import { ProcessRejectedAgain } from "../../history/history-rejected-again";
import { NeedFileActions, type NeedNotice } from "./needs-file-actions";
import {
  FAILED_JOBS_LIMIT,
  NEEDS_A_LOOK_PATH,
  buildNeeds,
  needCount,
  type NeedGroup,
  type NeedRow,
} from "./needs-model";
import { useNeedsFiles } from "./needs-files";

type RowHandlers = {
  /** Which kind of media each workflow holds, by id. */
  mediaScopes: ReadonlyMap<number, "movie" | "tv">;
  onNotice: (notice: NeedNotice) => void;
  onOpen: ((file: ProcessingFile) => void) | undefined;
};

function NeedItem({
  row,
  mediaScopes,
  onNotice,
  onOpen,
}: { row: NeedRow } & RowHandlers) {
  return (
    <li className="mm-need">
      <b className="mm-need__title">{row.title}</b>
      {row.file ? (
        <small className="mm-need__workflow">{row.file.library_name}</small>
      ) : null}
      <span className="mm-need__reason">{row.reason}</span>
      <span className="mm-need__actions">
        {row.file ? (
          <NeedFileActions
            file={row.file}
            mediaScope={mediaScopes.get(row.file.library_id) ?? "movie"}
            onNotice={onNotice}
            onOpen={onOpen}
          />
        ) : null}
        {row.link ? (
          <Link className="mm-need__link" to={row.link.to}>
            {row.link.label} →
          </Link>
        ) : null}
      </span>
    </li>
  );
}

function Group({
  group,
  workflowId,
  workflowName,
  ...handlers
}: {
  group: NeedGroup;
  workflowId: number | null | undefined;
  workflowName: string | undefined;
} & RowHandlers) {
  return (
    <section aria-label={group.title} className="mm-needs__group">
      <header className="mm-needs__group-head">
        <h3 className="mm-needs__group-title">{group.title}</h3>
        {group.rejected ? (
          <ProcessRejectedAgain
            libraryId={workflowId ?? undefined}
            libraryName={workflowName}
          />
        ) : null}
      </header>
      <ul className="mm-needs__rows">
        {group.rows.map((row) => (
          <NeedItem key={row.key} row={row} {...handlers} />
        ))}
      </ul>
      {group.more > 0 ? (
        <Link className="mm-need__link mm-needs__more" to={NEEDS_A_LOOK_PATH}>
          and {group.more.toLocaleString()} more in History →
        </Link>
      ) : null}
    </section>
  );
}

function AllClear() {
  return (
    <p className="mm-needs__clear">
      <svg
        viewBox="0 0 24 24"
        width="16"
        height="16"
        fill="none"
        stroke="currentColor"
        strokeWidth="2.4"
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden="true"
      >
        <path d="m5 12 5 5 9-10" />
      </svg>
      <b>All clear</b>
      <span>Nothing needs you right now.</span>
    </p>
  );
}

type NeedsPanelProps = {
  workflows: readonly ProcessingLibrary[] | undefined;
  /** The files that failed, from the page's own list. */
  stuck: readonly ProcessingFile[];
  /** How many files the rules rejected, from the files list's per-status counts. */
  rejectedCount: number;
  /** Narrows the panel to one workflow's files. */
  workflowId?: number | null;
  /** Opens a file's story. Without it, Open goes to the file in History. */
  onOpen?: (file: ProcessingFile) => void;
};

/**
 * The files and conditions that wait on a person, grouped by what went wrong, each file with its own actions.
 * Says "All clear" when there are none.
 */
export function NeedsPanel({
  workflows,
  stuck,
  rejectedCount,
  workflowId,
  onOpen,
}: NeedsPanelProps) {
  const readiness = useSystemReadinessQuery();
  // Leaves out a file the owner has since removed from History: this panel is about current problems, not a
  // record of every failure Weir has ever seen (System › Jobs keeps that).
  const failedJobs = useProcessingJobsInspectionQuery(
    "failed",
    FAILED_JOBS_LIMIT,
    true,
  );
  const others = useNeedsFiles(workflowId, rejectedCount);
  const [notice, setNotice] = useState<NeedNotice | null>(null);
  const groups = buildNeeds({
    workflows,
    workflowId,
    readiness: readiness.data,
    failedJobCount: failedJobs.data?.jobs.length ?? 0,
    failed: stuck,
    others,
  });
  const total = needCount(groups);
  const mediaScopes = new Map(
    (workflows ?? []).map((workflow) => [workflow.id, workflow.media_type]),
  );
  const workflowName = workflows?.find(
    (workflow) => workflow.id === workflowId,
  )?.name;
  return (
    <Panel
      title="Needs you"
      count={
        total === 0 ? undefined : (
          <span className="mm-needs__count">
            {total.toLocaleString()} to look at
          </span>
        )
      }
      to={NEEDS_A_LOOK_PATH}
      toLabel="History"
    >
      {notice ? (
        <p
          className="mm-needs__notice"
          role={notice.failed ? "alert" : "status"}
        >
          {notice.text}
        </p>
      ) : null}
      {groups.length === 0 ? (
        <AllClear />
      ) : (
        <div className="mm-needs__list" data-testid="live-needs">
          {groups.map((group) => (
            <Group
              key={group.key}
              group={group}
              workflowId={workflowId}
              workflowName={workflowName}
              mediaScopes={mediaScopes}
              onNotice={setNotice}
              onOpen={onOpen}
            />
          ))}
        </div>
      )}
    </Panel>
  );
}
