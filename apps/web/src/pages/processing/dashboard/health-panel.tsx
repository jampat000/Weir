import { Link } from "react-router-dom";

import { Chip } from "../../../components/panels/chip";
import { Panel } from "../../../components/panels/panel";
import {
  workflowBadgeLabel,
  workflowKindOf,
} from "../../../lib/processing/workflow-kind";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import { problemCount } from "./health-model";
import { useHealth } from "./use-health";

const MANAGERS_PATH = "/settings?tab=media-managers";
const ABOUT_PATH = "/system";

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

/**
 * How well Weir is set up to do its work: each workflow's folder chain, whether the media managers and
 * download clients answer, and the tools it writes with. Each part loads and fails on its own.
 */
export function HealthPanel({
  workflows,
}: {
  workflows: readonly ProcessingLibrary[];
}) {
  const health = useHealth(workflows);
  const problems = problemCount(health);
  return (
    <Panel
      title="Health"
      count={problems === 0 ? "all clear" : `${problems} to look at`}
    >
      <div data-testid="live-health" className="mm-health__scroll">
        <Section title="Workflows">
          {health.workflows.length === 0 ? (
            <p className="mm-health__empty">No workflow is switched on.</p>
          ) : (
            <ul className="mm-health__list">
              {health.workflows.map(({ workflow, verdict }) => (
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
                </li>
              ))}
            </ul>
          )}
        </Section>
        <Section title="Connections">
          {health.connections.length === 0 ? (
            <p className="mm-health__empty">
              No media manager or download client is connected.
            </p>
          ) : (
            <ul className="mm-health__pills">
              {health.connections.map((pill) => (
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
