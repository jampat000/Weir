import { Link } from "react-router-dom";

import type { ConnectionLight } from "../../../lib/connections/connection-lights";
import {
  answerWords,
  type ConnectionEntry,
  type ConnectionState,
} from "../../../lib/connections/connection-model";
import { classNames } from "../../../lib/ui/class-names";
import { checkedAgo } from "./health-model";

const ROLE_WORDS: Record<ConnectionEntry["kind"], string> = {
  media_manager: "manager",
  download_client: "client",
};

/** What the state is, in words, for a screen reader: the dot's colour is never the only thing that says it. */
const STATE_WORDS: Record<ConnectionState, string> = {
  ok: "answering",
  slow: "slow to answer",
  down: "not answering",
  off: "switched off",
  untested: "not tested yet",
};

/** The right-hand words of a row: "12s ago · 84 ms", or why there is nothing to say yet. */
export function whenWords(entry: ConnectionEntry, now: number): string {
  if (entry.state === "untested") return STATE_WORDS.untested;
  const when = entry.checkedAt === null ? "" : checkedAgo(entry.checkedAt, now);
  if (entry.state === "down")
    return when ? `${STATE_WORDS.down} · ${when}` : STATE_WORDS.down;
  const took = entry.answerMs === null ? "" : answerWords(entry.answerMs);
  return [when, took].filter(Boolean).join(" · ") || STATE_WORDS.ok;
}

type ConnectionLiveRowProps = {
  entry: ConnectionEntry;
  light: ConnectionLight | null;
  now: number;
  to: string;
};

/** One switched-on connection on the Health panel: its dot, its name, what it is, and when it last answered. */
export function ConnectionLiveRow({
  entry,
  light,
  now,
  to,
}: ConnectionLiveRowProps) {
  return (
    <li
      className={classNames(
        "mm-conn",
        `mm-conn--${entry.state}`,
        light && `mm-conn--${light}`,
      )}
      data-fit=""
      data-testid="live-connection"
    >
      <Link to={to} className="mm-conn__link" title={entry.name}>
        <span className="mm-conn__dot" aria-hidden="true" />
        <span className="mm-conn__name">
          <span className="mm-conn__base">{entry.baseName}</span>
          {entry.nickname ? (
            <span className="mm-conn__nickname"> · {entry.nickname}</span>
          ) : null}
        </span>
        <span className="mm-conn__role">{ROLE_WORDS[entry.kind]}</span>
        <span className="mm-conn__when">
          <span className="sr-only">{STATE_WORDS[entry.state]}. </span>
          <time
            dateTime={
              entry.checkedAt === null
                ? undefined
                : new Date(entry.checkedAt).toISOString()
            }
          >
            {whenWords(entry, now)}
          </time>
        </span>
      </Link>
    </li>
  );
}
