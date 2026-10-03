import { Link } from "react-router-dom";

import { StatusDot } from "../../../components/panels/status-dot";
import {
  connectionRowMeaning,
  type ConnectionLight,
} from "../../../lib/connections/connection-lights";
import {
  answerWords,
  connectionNameWords,
  needsALook,
  type ConnectionEntry,
  type ConnectionState,
} from "../../../lib/connections/connection-model";
import { classNames } from "../../../lib/ui/class-names";
import { FitText } from "../../../lib/ui/fit-text";
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
      className={classNames("mm-conn", light && "mm-conn--lit")}
      data-status={connectionRowMeaning(entry.state, light)}
      data-fit=""
      data-testid="live-connection"
    >
      <Link to={to} className="mm-conn__link" title={entry.name}>
        <StatusDot meaning={connectionRowMeaning(entry.state, light)} />
        <span className="sr-only">{entry.name}</span>
        <FitText
          className="mm-conn__name"
          words={connectionNameWords(entry)}
          title={entry.name}
          ariaHidden
        />
        <span className="mm-conn__role">{ROLE_WORDS[entry.kind]}</span>
        <span
          className={classNames(
            "mm-conn__when",
            needsALook(entry) && "mm-status-text",
          )}
        >
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
