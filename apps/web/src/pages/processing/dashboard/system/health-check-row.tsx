import { Link } from "react-router-dom";

import { StatusDot } from "../../../../components/panels/status-dot";
import { classNames } from "../../../../lib/ui/class-names";
import { needsYou } from "../../../../lib/ui/status-meaning";
import { checkedAgo } from "../health-model";
import { HEALTH_AREAS, type HealthCheck } from "./health-checks";

const AREA_NAMES = new Map(HEALTH_AREAS.map(({ key, name }) => [key, name]));

type HealthCheckRowProps = {
  check: HealthCheck;
  now: number;
  /** Looking at this check's subject again is under way. */
  checking: boolean;
  onAgain: (check: HealthCheck) => void;
  /** Opens the workflow's whole folder chain; given only for a check about a workflow. */
  onDetails: ((workflowId: number) => void) | null;
};

/**
 * One check: a dot, what it is and why in a few words, and what can be done about it: "Fix it →" to the screen that
 * sets it, "Check again", and for a workflow "Details". Why is one line, the whole sentence being its tooltip (and, for
 * a workflow, what Details say). A problem has the area it is in and when it was last looked at beneath; a check
 * that is fine is one tight row with that on its right, and its "Check again" shows when the row is pointed at.
 */
export function HealthCheckRow({
  check,
  now,
  checking,
  onAgain,
  onDetails,
}: HealthCheckRowProps) {
  const { workflowId } = check;
  const problem = needsYou(check.meaning);
  const area = AREA_NAMES.get(check.area);
  const checked =
    check.checkedAt === null
      ? ""
      : `checked ${checkedAgo(check.checkedAt, now)}`;
  return (
    <li
      className={classNames("mm-sy-check", !problem && "mm-sy-check--fine")}
      data-status={check.meaning}
      data-fit=""
      data-testid="system-check"
    >
      <StatusDot meaning={check.meaning} />
      <span className="mm-sy-check__text">
        <b title={check.title}>{check.title}</b>
        <span className="mm-sy-check__why" title={check.why}>
          {check.words}
        </span>
        {problem ? (
          <em className="mm-sy-check__meta">
            <i>{area}</i>
            {checked}
          </em>
        ) : null}
      </span>
      <span className="mm-sy-check__actions">
        {problem ? null : (
          <span className="mm-sy-check__when">
            {[area, checked].filter(Boolean).join(" · ")}
          </span>
        )}
        {check.fix ? (
          <Link
            className="mm-sy-btn"
            to={check.fix.to}
            title={check.fix.note}
            aria-label={`${check.fix.label} ${check.title}`}
          >
            {check.fix.label}
          </Link>
        ) : null}
        <button
          type="button"
          className={classNames("mm-sy-btn", !problem && "mm-sy-btn--quiet")}
          disabled={checking}
          aria-label={`Check again: ${check.title}`}
          title="Looks at this again now."
          onClick={() => onAgain(check)}
        >
          {checking ? "Checking…" : "Check again"}
        </button>
        {workflowId !== null && onDetails ? (
          <button
            type="button"
            className="mm-sy-btn"
            aria-label={`Details: ${check.title}`}
            title="Opens every line of this workflow's folder chain."
            onClick={() => onDetails(workflowId)}
          >
            Details
          </button>
        ) : null}
      </span>
    </li>
  );
}
