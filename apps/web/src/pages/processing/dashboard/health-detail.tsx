import { Link } from "react-router-dom";

import { Chip } from "../../../components/panels/chip";
import { Panel } from "../../../components/panels/panel";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import { useNow } from "../../../lib/ui/use-now";
import { useCheckNow } from "./check-now";
import { connectionDetails, diskRows } from "./health-detail-model";
import { WorkflowsDetail } from "./health-detail-workflows";
import { useHealth, type Health } from "./use-health";

const MANAGERS_PATH = "/settings?tab=media-managers";
const ABOUT_PATH = "/system";
/** Seconds are shown, so "answered 12s ago" moves once a second. */
const TICK_MS = 1000;

function ConnectionsDetail({ health, now }: { health: Health; now: number }) {
  const connections = connectionDetails(
    health.managers,
    health.downloadClients,
    now,
  );
  return (
    <Panel title="Connections" to={MANAGERS_PATH} toLabel="Manage">
      <div className="mm-health-detail__body">
        {connections.length === 0 ? (
          <p className="mm-health__empty">Nothing connected.</p>
        ) : (
          <ul className="mm-health-detail__rows">
            {connections.map((connection) => (
              <li key={connection.key} className="mm-health-detail__row">
                <div className="mm-health-detail__row-head">
                  <b>{connection.name}</b>
                  <Chip tone={connection.tone}>{connection.state}</Chip>
                </div>
                <p className="mm-health__empty">
                  {connection.role}, {connection.kind}
                  {connection.address ? ` · ${connection.address}` : ""}
                </p>
                {connection.detail ? (
                  <p className="mm-health__empty">{connection.detail}</p>
                ) : null}
              </li>
            ))}
          </ul>
        )}
      </div>
    </Panel>
  );
}

function ToolsDetail({ tools }: { tools: Health["tools"] }) {
  return (
    <Panel title="Tools" to={ABOUT_PATH} toLabel="About">
      <div className="mm-health-detail__body">
        {tools === null ? (
          <p className="mm-health__empty">Reading the tools…</p>
        ) : (
          <ul className="mm-health-detail__rows">
            {tools.map((tool) => (
              <li key={tool.key} className="mm-health-detail__row">
                <div className="mm-health-detail__row-head">
                  <b>{tool.name}</b>
                  <Chip tone={tool.tone}>{tool.version}</Chip>
                </div>
                <p className="mm-health__empty mm-health-detail__mono">
                  {tool.banner}
                </p>
              </li>
            ))}
          </ul>
        )}
      </div>
    </Panel>
  );
}

function DiskDetail({
  workflows,
}: {
  workflows: readonly ProcessingLibrary[];
}) {
  return (
    <Panel title="Disk space">
      <div className="mm-health-detail__body">
        <p className="mm-health__empty" data-testid="health-disk-note">
          Weir holds a file that would leave less than this free.
        </p>
        <ul className="mm-health-detail__rows">
          {diskRows(workflows).map((row) => (
            <li key={row.key} className="mm-health-detail__row">
              <div className="mm-health-detail__row-head">
                <Link to={`/settings?tab=libraries&edit=${row.key}`}>
                  <b>{row.workflow}</b>
                </Link>
                <span>Keeps {row.keepFree} free</span>
              </div>
              <p className="mm-health__empty mm-health-detail__mono">
                {row.outputFolder}
              </p>
            </li>
          ))}
        </ul>
      </div>
    </Panel>
  );
}

type HealthDetailProps = {
  workflows: readonly ProcessingLibrary[];
  /** Narrows the view to one workflow: its chain, its connections and its drive. */
  workflowId?: number | null;
};

/**
 * Dashboard › System: every workflow with its whole folder chain, the media managers and download clients with
 * what each last said, the tools with their versions, and the room Weir keeps free where it writes.
 */
export function HealthDetail({ workflows, workflowId }: HealthDetailProps) {
  const health = useHealth(workflows, workflowId);
  const check = useCheckNow(health);
  const now = useNow(TICK_MS);
  const inScope = workflows.filter(
    (workflow) => workflowId == null || workflow.id === workflowId,
  );
  return (
    <div className="mm-health-detail" data-testid="health-detail">
      <div className="mm-health-detail__main">
        <WorkflowsDetail
          health={health}
          check={check}
          switchedOff={inScope.filter((workflow) => !workflow.enabled).length}
        />
      </div>
      <div className="mm-health-detail__side">
        <ConnectionsDetail health={health} now={now} />
        <ToolsDetail tools={health.tools} />
        <DiskDetail
          workflows={inScope.filter((workflow) => workflow.enabled)}
        />
      </div>
    </div>
  );
}
