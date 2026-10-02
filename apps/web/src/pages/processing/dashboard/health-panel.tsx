import { Link, useSearchParams } from "react-router-dom";

import { Chip } from "../../../components/panels/chip";
import { Panel } from "../../../components/panels/panel";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import {
  workflowBadgeLabel,
  workflowKindOf,
} from "../../../lib/processing/workflow-kind";
import { useNow } from "../../../lib/ui/use-now";
import { firstSentence } from "../processing-model";
import { CheckNowButton, useCheckNow } from "./check-now";
import { connectionPills, healthSummary, problemCount } from "./health-model";
import { useHealth } from "./use-health";

const MANAGERS_PATH = "/settings?tab=media-managers";
const ABOUT_PATH = "/system";
/** The dashboard's System view, where Health is shown in full. */
const SYSTEM_VIEW = "system";
/** Seconds are shown, so "answered 12s ago" moves once a second. */
const TICK_MS = 1000;

function Section({
  title,
  children,
}: {
  title: string;
  children: React.ReactNode;
}) {
  return (
    <section aria-label={title}>
      <h3 className="mm-health__heading">{title}</h3>
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
 * media managers and download clients answered, and the tools it writes with. Each part loads and fails on its
 * own. "Check now" reads the folders again and tests every connection; "Full detail" opens the System view.
 */
export function HealthPanel({ workflows, workflowId }: HealthPanelProps) {
  const health = useHealth(workflows, workflowId);
  const check = useCheckNow(health);
  const now = useNow(TICK_MS);
  const [search] = useSearchParams();
  const detail = new URLSearchParams(search);
  detail.set("view", SYSTEM_VIEW);
  const connections = connectionPills(
    health.managers,
    health.downloadClients,
    now,
  );
  const problems = problemCount({ ...health, connections });
  return (
    <Panel
      title="Health"
      count={healthSummary(problems, health.workflows)}
      aside={<CheckNowButton check={check} />}
      to={`?${detail.toString()}`}
      toLabel="Full detail"
      iconOnly
    >
      <div data-testid="live-health" className="mm-health__scroll">
        {check.notice ? (
          <p className="mm-health__notice" role="status">
            {check.notice}
          </p>
        ) : null}
        <Section title="Workflows">
          {health.workflows.length === 0 ? (
            <p className="mm-health__empty">No workflow switched on.</p>
          ) : (
            <ul className="mm-health__list">
              {health.workflows.map(({ workflow, verdict, why }) => (
                <li key={workflow.id} className="mm-health__row">
                  <Link
                    className="mm-health__name"
                    to={`/settings?tab=libraries&edit=${workflow.id}`}
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
            <p className="mm-health__empty">Nothing connected.</p>
          ) : (
            <ul className="mm-health__pills">
              {connections.map((pill) => (
                <li key={pill.key}>
                  <Link to={MANAGERS_PATH} className="mm-health__pill">
                    <Chip tone={pill.tone}>
                      {pill.name}{" "}
                      <span className="mm-health__state">{pill.state}</span>
                    </Chip>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </Section>
        <Section title="Tools">
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
