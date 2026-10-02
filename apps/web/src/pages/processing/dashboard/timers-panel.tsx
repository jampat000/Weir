import { Link } from "react-router-dom";

import { Panel } from "../../../components/panels/panel";
import { lineWords, waitFraction, type NextItem } from "./next-model";

const NOTHING_SCHEDULED = "Nothing scheduled. Switch on a workflow or cleanup.";
const PAUSED = "Paused · nothing new starts.";

type TimersPanelProps = {
  /** What Weir does on its own, soonest first. */
  items: readonly NextItem[];
  now: number;
  paused: boolean;
};

/** Everything Weir does on a timer, soonest first, each with how long until it runs. */
export function TimersPanel({ items, now, paused }: TimersPanelProps) {
  return (
    <Panel
      title="Timers"
      count={
        items.length === 0
          ? "none"
          : `${items.length.toLocaleString()} scheduled`
      }
      dataTestId="dashboard-timers"
    >
      <div className="mm-timers">
        {paused ? <p className="mm-timers__note">{PAUSED}</p> : null}
        {items.length === 0 ? (
          <p className="mm-timers__note">{NOTHING_SCHEDULED}</p>
        ) : (
          <ul className="mm-timers__list">
            {items.map((item) => (
              <li key={item.key} className="mm-timers__row">
                <Link to={item.to}>{item.label}</Link>
                <span>{lineWords(item.at, now)}</span>
                <span className="mm-next__bar" aria-hidden="true">
                  <i
                    style={{
                      width: `${Math.round((waitFraction(item, now) ?? 0) * 100)}%`,
                    }}
                  />
                </span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </Panel>
  );
}
