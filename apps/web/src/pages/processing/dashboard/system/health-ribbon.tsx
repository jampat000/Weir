import type { CSSProperties } from "react";

import { classNames } from "../../../../lib/ui/class-names";
import type { AreaTally, AreaTone } from "./health-card-model";
import type { HealthArea } from "./health-checks";

const TONE_COLOURS: Record<AreaTone, string> = {
  bad: "var(--mm-destructive)",
  warn: "var(--mm-warning)",
  ok: "var(--mm-success)",
};

type HealthRibbonProps = {
  tallies: readonly AreaTally[];
  /** The area picked as a filter, or null for none. */
  picked: HealthArea | null;
  onPick: (area: HealthArea | null) => void;
  /** A sheen sweeps across every area, as the checks are read again. */
  sheen: boolean;
};

/**
 * The areas as a ribbon of chevrons, each tinted by how its checks are doing and counting how many pass. Pressing one
 * lists all of its checks; pressing it again goes back to the problems.
 */
export function HealthRibbon({
  tallies,
  picked,
  onPick,
  sheen,
}: HealthRibbonProps) {
  return (
    <div className="mm-sy-areas" role="group" aria-label="Health areas">
      {tallies.map((tally) => (
        <button
          key={tally.key}
          type="button"
          className={classNames(
            "mm-sy-area",
            picked === tally.key && "mm-sy-area--on",
            sheen && "mm-sy-area--checking",
          )}
          style={{ "--tone": TONE_COLOURS[tally.tone] } as CSSProperties}
          title={`${tally.name}: ${tally.ok} of ${tally.total} pass`}
          aria-pressed={picked === tally.key}
          onClick={() => onPick(picked === tally.key ? null : tally.key)}
        >
          <span className="mm-sy-area__name">
            <span className="mm-sy-area__dot" aria-hidden="true">
              ●
            </span>
            {tally.name}
          </span>
          <span className="mm-sy-area__count">
            <b>{tally.ok}</b>/{tally.total}
          </span>
        </button>
      ))}
    </div>
  );
}
