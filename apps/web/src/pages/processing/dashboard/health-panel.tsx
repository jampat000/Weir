import { Link, useSearchParams } from "react-router-dom";

import { Chip } from "../../../components/panels/chip";
import { Panel } from "../../../components/panels/panel";
import { useConnections } from "../../../lib/connections/use-connections";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import {
  workflowBadgeLabel,
  workflowKindOf,
} from "../../../lib/processing/workflow-kind";
import { useNow } from "../../../lib/ui/use-now";
import { firstSentence } from "../processing-model";
import { CheckNowButton, useCheckNow } from "./check-now";
import { ConnectionLiveRow } from "./connection-live-row";
import { useFittingRows } from "./fit-rows";
import {
  healthSummary,
  problemCount,
  rowsLeftOut,
  type HealthUnit,
} from "./health-model";
import { useHealth } from "./use-health";
import {
  setupTabPath,
  workflowEditorPath,
} from "../../../lib/settings/setup-areas";

const MANAGERS_PATH = setupTabPath("managers");
const ABOUT_PATH = "/system";
/** The dashboard's System view, where Health is shown in full. */
const SYSTEM_VIEW = "system";
/** Seconds are shown, so "12s ago" moves once a second. */
const TICK_MS = 1000;

/**
 * A part of the panel, for the panel's fitting. A section of rows makes its heading a unit of its own, and each row
 * is another, so a heading is never left standing over rows that did not fit. A section that is `whole` shows or
 * hides all together.
 */
function Section({
  title,
  children,
  fitting = "by row",
}: {
  title: string;
  children: React.ReactNode;
  fitting?: "by row" | "whole";
}) {
  const whole = fitting === "whole";
  return (
    <section aria-label={title} data-fit={whole ? "" : undefined}>
      <h3 className="mm-health__heading" data-fit={whole ? undefined : ""}>
        {title}
      </h3>
      {children}
    </section>
  );
}

type HealthPanelProps = {
  workflows: readonly ProcessingLibrary[];
  /** Narrows the panel to one workflow: its verdict, its connections. */
  workflowId?: number | null;
};

/**
 * How well Weir is set up to do its work: each workflow's folder chain with why it is not in sync, whether the
 * media managers and download clients answer, a row each that lights while Weir talks to it, and the tools it writes
 * with. The panel's height decides how many whole rows show, and the header says how many more there are. Each part
 * loads and fails on its own. "Check now" reads the folders again and tests every connection; "Full detail" opens
 * the System view.
 */
export function HealthPanel({ workflows, workflowId }: HealthPanelProps) {
  const health = useHealth(workflows, workflowId);
  const check = useCheckNow(health);
  const now = useNow(TICK_MS);
  const [search] = useSearchParams();
  const [bodyRef, fits] = useFittingRows();
  const detail = new URLSearchParams(search);
  detail.set("view", SYSTEM_VIEW);
  const detailPath = `?${detail.toString()}`;
  const { entries, lights } = useConnections(
    health.managers,
    health.downloadClients,
  );
  const connections = entries.filter((entry) => entry.enabled);
  const problems = problemCount({ ...health, connections });
  const units: HealthUnit[] = [
    "heading",
    ...(health.workflows.length === 0
      ? ["note" as const]
      : health.workflows.map(() => "row" as const)),
    "heading",
    ...(connections.length === 0
      ? ["note" as const]
      : connections.map(() => "row" as const)),
    "tools",
  ];
  const more = rowsLeftOut(units, fits);
  return (
    <Panel
      title="Health"
      count={healthSummary(problems, health.workflows)}
      aside={<CheckNowButton check={check} />}
      to={detailPath}
      toLabel="Full detail"
      toText={more > 0 ? `${more.toLocaleString()} more` : undefined}
      iconOnly={more === 0}
    >
      <div ref={bodyRef} data-testid="live-health" className="mm-health__fit">
        {check.notice ? (
          <p className="mm-health__notice" role="status">
            {check.notice}
          </p>
        ) : null}
        <Section title="Workflows">
          {health.workflows.length === 0 ? (
            <p className="mm-health__empty" data-fit="">
              No workflow switched on.
            </p>
          ) : (
            <ul className="mm-health__list">
              {health.workflows.map(({ workflow, verdict, why }) => (
                <li key={workflow.id} className="mm-health__row" data-fit="">
                  <Link
                    className="mm-health__name"
                    to={workflowEditorPath(workflow.id)}
                  >
                    <b>{workflow.name}</b>
                    <small>
                      {workflowBadgeLabel(
                        workflowKindOf(workflow, health.managers),
                      )}
                    </small>
                  </Link>
                  <Chip tone={verdict.tone}>{verdict.words}</Chip>
                  {why ? (
                    <p className="mm-health__why" title={why}>
                      {firstSentence(why)}
                    </p>
                  ) : null}
                </li>
              ))}
            </ul>
          )}
        </Section>
        <Section title="Connections">
          {connections.length === 0 ? (
            <p className="mm-health__empty" data-fit="">
              Nothing connected.
            </p>
          ) : (
            <ul className="mm-conn-list">
              {connections.map((entry) => (
                <ConnectionLiveRow
                  key={entry.key}
                  entry={entry}
                  light={lights.get(entry.key) ?? null}
                  now={now}
                  to={MANAGERS_PATH}
                />
              ))}
            </ul>
          )}
        </Section>
        <Section title="Tools" fitting="whole">
          {health.tools === null ? (
            <p className="mm-health__empty">Reading the tools…</p>
          ) : (
            <ul className="mm-health__pills">
              {health.tools.map((tool) => (
                <li key={tool.key}>
                  <Link to={ABOUT_PATH} className="mm-health__pill">
                    <Chip tone={tool.tone}>
                      {tool.name}{" "}
                      <span className="mm-health__state">{tool.version}</span>
                    </Chip>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </Section>
      </div>
    </Panel>
  );
}
