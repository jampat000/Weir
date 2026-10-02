import { Link } from "react-router-dom";

import { Chip } from "../../../../components/panels/chip";
import { StoryPanelShell } from "../../../../components/processing/story-panel-shell";
import { FolderChainSections } from "../../../../components/shared/folder-chain-sections";
import type { MediaManagerConnection } from "../../../../lib/media-managers/media-managers-api";
import {
  workflowBadgeLabel,
  workflowKindNote,
  workflowKindOf,
} from "../../../../lib/processing/workflow-kind";
import type { WorkflowHealth } from "../use-health";
import { keepFreeWords } from "./keep-free-words";
import { MANAGERS_PATH, workflowPath } from "./system-paths";

type WorkflowChainDrawerProps = {
  item: WorkflowHealth;
  managers: MediaManagerConnection[];
  onClose: () => void;
};

/**
 * A workflow's whole folder chain in a slide-over: its kind, its verdict and why, every line of the chain grouped by
 * the place that has an opinion, the room Weir keeps free where it writes, and links to where each part is set.
 */
export function WorkflowChainDrawer({
  item,
  managers,
  onClose,
}: WorkflowChainDrawerProps) {
  const { workflow, verdict, why, chain } = item;
  const kind = workflowKindOf(workflow, managers);
  return (
    <StoryPanelShell
      eyebrow="Workflow"
      title={workflow.name}
      backdropLabel="Close the workflow's details"
      testId="system-workflow-detail"
      onClose={onClose}
    >
      <div className="mm-sy-detail">
        <div className="mm-sy-detail__head">
          <span>{workflowBadgeLabel(kind)}</span>
          <Chip tone={verdict.tone}>{verdict.words}</Chip>
        </div>
        <p className="mm-sy-detail__note">{workflowKindNote(kind)}</p>
        {why ? <p className="mm-sy-detail__why">{why}</p> : null}
        <div className="mm-sy-detail__chain">
          {chain ? (
            <FolderChainSections chain={chain} />
          ) : (
            <p className="mm-sy-note">Folder check pending.</p>
          )}
        </div>
        <p className="mm-sy-detail__note">
          Keeps {keepFreeWords(workflow)} free where it writes.
        </p>
        <p className="mm-sy-detail__links">
          <Link to={workflowPath(workflow.id)}>Open this workflow</Link>
          {kind.kind === "linked" ? (
            <Link to={MANAGERS_PATH}>Media managers</Link>
          ) : null}
        </p>
      </div>
    </StoryPanelShell>
  );
}
