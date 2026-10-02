import { Panel } from "../../../components/panels/panel";
import type { ConnectionLight } from "../../../lib/connections/connection-lights";
import type { ConnectionEntry } from "../../../lib/connections/connection-model";
import { classNames } from "../../../lib/ui/class-names";
import { useFittingRows } from "./fit-rows";
import { FitText } from "./system/fit-words";
import { MoreCount } from "./system/more-count";
import {
  checkedWords,
  connectionSub,
  connectionTooltip,
  connectionsLineWords,
  groupConnections,
} from "./connections-card-model";
import type { ConnectionTesting } from "./use-connection-testing";
import { setupTabPath } from "../../../lib/settings/setup-areas";

const MANAGERS_PATH = setupTabPath("managers");
const TEST_ALL_HINT =
  "Asks every connection that is switched on and shows each answer here.";

type ConnectionRowProps = {
  entry: ConnectionEntry;
  light: ConnectionLight | null;
  testing: ConnectionTesting;
  now: number;
};

function ConnectionRow({ entry, light, testing, now }: ConnectionRowProps) {
  const busy = testing.testing.has(entry.key);
  const shown = busy ? "asking" : light;
  return (
    <li
      className={classNames(
        "mm-conn",
        "mm-ctable__row",
        `mm-conn--${entry.state}`,
        shown && `mm-conn--${shown}`,
      )}
      title={connectionTooltip(entry)}
      data-fit=""
      data-testid="system-connection"
    >
      <span className="mm-conn__dot" aria-hidden="true" />
      <span className="mm-ctable__name">
        <b>{entry.name}</b>
        <span>{connectionSub(entry)}</span>
      </span>
      <span className="mm-ctable__checked">
        {checkedWords(entry, busy, now)}
      </span>
      {entry.testable && !busy ? (
        <button
          type="button"
          className="mm-ctable__test"
          aria-label={`Test ${entry.name}`}
          onClick={() => void testing.test(entry)}
        >
          Test
        </button>
      ) : null}
    </li>
  );
}

type ConnectionsCardProps = {
  entries: readonly ConnectionEntry[];
  lights: ReadonlyMap<string, ConnectionLight>;
  testing: ConnectionTesting;
  now: number;
};

/**
 * Dashboard › System: every media manager and download client in its group, with how it is answering and when it was
 * last checked. A row lights blue while it is tested, then green or red, and lights the same way when the server
 * pushes that Weir asked it or it called Weir. A Test button on each row, and one for all of them, runs the call
 * Settings makes.
 */
export function ConnectionsCard({
  entries,
  lights,
  testing,
  now,
}: ConnectionsCardProps) {
  const groups = groupConnections(entries);
  const [listRef, fits] = useFittingRows();
  // The bars and rows in the order they are measured: each group's bar, then its rows. Only a row counts as left out.
  const units = groups.flatMap((group) => [
    false,
    ...group.rows.map(() => true),
  ]);
  const more = units.slice(fits).filter(Boolean).length;
  const testable = entries.filter((entry) => entry.testable);
  return (
    <Panel
      title="Connections"
      count={
        <FitText
          words={connectionsLineWords(entries)}
          className="mm-sy-count"
        />
      }
      aside={
        <>
          <MoreCount count={more} />
          <button
            type="button"
            className="mm-sy-btn mm-sy-btn--tall"
            title={TEST_ALL_HINT}
            aria-label="Test all connections"
            disabled={testing.allBusy || testable.length === 0}
            onClick={() => void testing.testAll(entries)}
          >
            {testing.allBusy ? "Testing…" : "Test all"}
          </button>
        </>
      }
      to={MANAGERS_PATH}
      toLabel="Manage"
    >
      <div ref={listRef} className="mm-ctable">
        {groups.length === 0 ? (
          <p className="mm-health__empty mm-ctable__empty">
            Nothing connected yet. Add a media manager or a download client and
            it shows here.
          </p>
        ) : null}
        {groups.map((group) => (
          <section
            key={group.kind}
            aria-label={group.title}
            className="mm-ctable__group"
          >
            <h3
              className={`mm-ctable__bar mm-ctable__bar--${group.tone}`}
              data-fit="with-next"
            >
              <span>{group.title}</span>
              <span className="mm-ctable__badge">{group.badge}</span>
            </h3>
            <ul className="mm-ctable__rows">
              {group.rows.map((entry) => (
                <ConnectionRow
                  key={entry.key}
                  entry={entry}
                  light={lights.get(entry.key) ?? null}
                  testing={testing}
                  now={now}
                />
              ))}
            </ul>
          </section>
        ))}
      </div>
    </Panel>
  );
}
