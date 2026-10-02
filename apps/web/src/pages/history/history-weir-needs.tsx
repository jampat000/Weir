import { Link } from "react-router-dom";

import { Panel } from "../../components/panels/panel";
import type { NeedRow } from "../processing/dashboard/needs-model";

/**
 * What is wrong with Weir itself (no workflow, stopped work, failed jobs), above the files in History's Needs you
 * view: none of it is a file, but the sidebar's badge counts it, so the view lists it for the two to add up.
 */
export function HistoryWeirNeeds({ rows }: { rows: readonly NeedRow[] }) {
  if (rows.length === 0) return null;
  return (
    <Panel title="Weir itself" count={`${rows.length.toLocaleString()} to fix`}>
      <ul className="mm-needs__rows" data-testid="history-weir-needs">
        {rows.map((row) => (
          <li key={row.key} className="mm-need" title={row.detail}>
            <b className="mm-need__title">{row.title}</b>
            <span className="mm-need__reason">{row.reason}</span>
            {row.link ? (
              <span className="mm-need__actions">
                <Link className="mm-need__link" to={row.link.to}>
                  {row.link.label} →
                </Link>
              </span>
            ) : null}
          </li>
        ))}
      </ul>
    </Panel>
  );
}
