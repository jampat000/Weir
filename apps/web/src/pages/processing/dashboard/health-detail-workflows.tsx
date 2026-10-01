import { Link } from "react-router-dom";

import { Chip } from "../../../components/panels/chip";
import { Panel } from "../../../components/panels/panel";
import { FolderChainSections } from "../../../components/shared/folder-chain-sections";
import {
  workflowBadgeLabel,
  workflowKindNote,
  workflowKindOf,
} from "../../../lib/processing/workflow-kind";
import { plural } from "../../../lib/ui/mm-plural";
import { CheckNowButton, type CheckNow } from "./check-now";
import type { Health, WorkflowHealth } from "./use-health";

const MANAGERS_PATH = "/settings?tab=media-managers";

function WorkflowDetail({
  item,
  health,
}: {
  item: WorkflowHealth;
  health: Health;
}) {
  const { workflow, verdict, why, chain } = item;
  const kind = workflowKindOf(workflow, health.managers);
  return (
    <li className="mm-health-detail__workflow" data-testid="health-workflow">
      <header className="mm-health-detail__workflow-head">
        <div className="mm-health-detail__workflow-name">
          <b>{workflow.name}</b>
          <span>{workflowBadgeLabel(kind)}</span>
        </div>
        <Chip tone={verdict.tone}>{verdict.words}</Chip>
      </header>
      <p className="mm-health__empty">{workflowKindNote(kind)}</p>
      {why ? <p className="mm-health__why">{why}</p> : null}
      <div className="mm-health-detail__chain">
        {chain ? (
          <FolderChainSections chain={chain} />
        ) : (
          <p className="mm-health__empty">
            The folder check has not answered yet.
          </p>
        )}
      </div>
      <p className="mm-health-detail__links">
        <Link to={`/settings?tab=libraries&edit=${workflow.id}`}>
          Open this workflow
        </Link>
        {kind.kind === "linked" ? (
          <Link to={MANAGERS_PATH}>Media managers</Link>
        ) : null}
      </p>
    </li>
  );
}

/** Every switched-on workflow with its kind, its verdict and every line of its folder chain, grouped. */
export function WorkflowsDetail({
  health,
  check,
  switchedOff,
}: {
  health: Health;
  check: CheckNow;
  /** How many workflows are switched off, so they are not mistaken for missing. */
  switchedOff: number;
}) {
  return (
    <Panel
      title="Workflows"
      count={
        switchedOff > 0
          ? `${plural(switchedOff, "other is", "others are")} switched off`
          : undefined
      }
      aside={<CheckNowButton check={check} />}
    >
      <div className="mm-health-detail__body">
        {check.notice ? (
          <p className="mm-health__notice" role="status">
            {check.notice}
          </p>
        ) : null}
        {health.workflows.length === 0 ? (
          <p className="mm-health__empty">No workflow is switched on.</p>
        ) : (
          <ul className="mm-health-detail__workflows">
            {health.workflows.map((item) => (
              <WorkflowDetail
                key={item.workflow.id}
                item={item}
                health={health}
              />
            ))}
          </ul>
        )}
      </div>
    </Panel>
  );
}
