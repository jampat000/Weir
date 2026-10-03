import type { ReactNode } from "react";

import { Chip } from "../../../../components/panels/chip";
import {
  CONNECTION_STATE_MEANING,
  type ConnectionState,
} from "../../../../lib/connections/connection-model";

/** What the health chip reads from a media manager or a download client. */
type TestedConnection = {
  enabled: boolean;
  last_test_at?: string | null;
  last_test_ok?: boolean | null;
  last_test_detail?: string | null;
};

/**
 * A result only counts with a time behind it. "Answering" beside "Last checked: never" is two statements that
 * cannot both be true, so a result with no check time reads as unchecked.
 */
export function lastResult(connection: TestedConnection): boolean | null {
  return connection.last_test_at ? (connection.last_test_ok ?? null) : null;
}

function health(
  connection: TestedConnection,
  unchecked: string,
): { words: string; state: ConnectionState } {
  if (!connection.enabled) return { words: "Off", state: "off" };
  const result = lastResult(connection);
  if (result === null) return { words: unchecked, state: "untested" };
  return result
    ? { words: "Answering", state: "ok" }
    : { words: "Not answering", state: "down" };
}

/** Whether the connection answers, or that it is switched off, as one chip in the colours every other good or bad word in Weir uses. */
export function ConnectionHealthChip({
  connection,
  unchecked,
}: {
  connection: TestedConnection;
  /** What an enabled connection that has never been checked is called. */
  unchecked: string;
}) {
  const { words, state } = health(connection, unchecked);
  return <Chip meaning={CONNECTION_STATE_MEANING[state]}>{words}</Chip>;
}

/**
 * The status of a connection on its row: whether it answers, when it was last checked, and why not when it
 * does not. A good result is one word; what the endpoint said is only worth showing when something is wrong.
 */
export function ConnectionStatusLine({
  connection,
  fmt,
  unchecked,
  testId,
  children,
}: {
  connection: TestedConnection;
  fmt: (iso: string | null) => string;
  unchecked: string;
  testId: string;
  /** A further line under the status, such as what happens next. */
  children?: ReactNode;
}) {
  const failed = lastResult(connection) === false;
  return (
    <div className="mm-conn-row__status" data-testid={testId}>
      <ConnectionHealthChip connection={connection} unchecked={unchecked} />
      <span className="mm-conn-row__checked">
        Last checked:{" "}
        <span className="mm-conn-row__when">
          {connection.last_test_at ? fmt(connection.last_test_at) : "never"}
        </span>
      </span>
      {connection.enabled && failed && connection.last_test_detail ? (
        <p className="mm-conn-row__problem mm-status-text" data-status="broken">
          {connection.last_test_detail}
        </p>
      ) : null}
      {children}
    </div>
  );
}
