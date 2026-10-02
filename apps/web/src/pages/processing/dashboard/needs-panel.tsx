import { useState } from "react";
import { Link } from "react-router-dom";

import { Panel } from "../../../components/panels/panel";
import type { ProcessingFile } from "../../../lib/processing/files-api";
import { useProcessingLibrariesQuery } from "../../../lib/processing/libraries-queries";
import { ProcessRejectedAgain } from "../../history/history-rejected-again";
import { NeedFileActions, type NeedNotice } from "./needs-file-actions";
import { NEEDS_A_LOOK_PATH, type NeedGroup, type NeedRow } from "./needs-model";
import { NEEDS_PANEL_ID } from "./show-needs-panel";
import { useNeedsYou } from "./use-needs-you";

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
    <li className="mm-need" title={row.detail}>
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
      <span>Nothing needs you.</span>
    </p>
  );
}

type NeedsPanelProps = {
  /** Narrows the panel to one workflow's files. */
  workflowId?: number | null;
  /** Opens a file's story. Without it, Open goes to the file in History. */
  onOpen?: (file: ProcessingFile) => void;
};

/**
 * The files and conditions that wait on a person, grouped by what went wrong, each file with its own actions.
 * Says "All clear" when there are none.
 */
export function NeedsPanel({ workflowId, onOpen }: NeedsPanelProps) {
  const workflows = useProcessingLibrariesQuery().data;
  const { groups, count } = useNeedsYou(workflowId);
  const [notice, setNotice] = useState<NeedNotice | null>(null);
  const mediaScopes = new Map(
    (workflows ?? []).map((workflow) => [workflow.id, workflow.media_type]),
  );
  const workflowName = workflows?.find(
    (workflow) => workflow.id === workflowId,
  )?.name;
  return (
    <Panel
      id={NEEDS_PANEL_ID}
      tabIndex={-1}
      title="Needs you"
      count={
        count === 0 ? undefined : (
          <span className="mm-needs__count">
            {count.toLocaleString()} to look at
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
