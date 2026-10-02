/** What the System card says about connections: the groups they sit in, how each group fares, and each row's words. */
import type { ConnectionKind } from "../../../lib/connections/connection-activity";
import {
  type ConnectionEntry,
  type ConnectionState,
} from "../../../lib/connections/connection-model";
import { checkedAgo } from "./health-model";

/** How a group fares, worst first: any connection down, else any slow, else any not yet tested, else all answering. */
export type GroupTone = "down" | "slow" | "untested" | "ok";

export type ConnectionGroup = {
  kind: ConnectionKind;
  title: string;
  /** The group's rows, the worst of them first. */
  rows: ConnectionEntry[];
  tone: GroupTone;
  /** "4/5 OK": the answering ones of those that are switched on. */
  badge: string;
};

const GROUP_TITLES: Record<ConnectionKind, string> = {
  media_manager: "Media managers",
  download_client: "Download clients",
};
const GROUP_ORDER: readonly ConnectionKind[] = [
  "media_manager",
  "download_client",
];

const MS_PER_SECOND = 1000;

/** How long a slow answer took, in seconds to a tenth: "2.4 s". */
const secondsWords = (ms: number) => `${(ms / MS_PER_SECOND).toFixed(1)} s`;

const WORST_FIRST: Record<ConnectionState, number> = {
  down: 0,
  slow: 1,
  untested: 2,
  ok: 3,
  off: 4,
};

const byWorstThenName = (a: ConnectionEntry, b: ConnectionEntry) =>
  WORST_FIRST[a.state] - WORST_FIRST[b.state] || a.name.localeCompare(b.name);

const switchedOn = (entries: readonly ConnectionEntry[]) =>
  entries.filter((entry) => entry.enabled);

function toneOf(entries: readonly ConnectionEntry[]): GroupTone {
  const states = new Set(switchedOn(entries).map((entry) => entry.state));
  if (states.has("down")) return "down";
  if (states.has("slow")) return "slow";
  return states.has("untested") ? "untested" : "ok";
}

/** The connections in their groups, the worst of each group first; a group with nothing in it is left out. */
export function groupConnections(
  entries: readonly ConnectionEntry[],
): ConnectionGroup[] {
  return GROUP_ORDER.flatMap((kind) => {
    const rows = entries
      .filter((entry) => entry.kind === kind)
      .sort(byWorstThenName);
    if (rows.length === 0) return [];
    const answering = rows.filter((row) => row.state === "ok").length;
    return [
      {
        kind,
        title: GROUP_TITLES[kind],
        rows,
        tone: toneOf(rows),
        badge: `${answering}/${switchedOn(rows).length} OK`,
      },
    ];
  });
}

/** "4/5 answering · 1 slow · 1 down": the card's one line. */
export function connectionsLine(entries: readonly ConnectionEntry[]): string {
  const on = switchedOn(entries);
  if (on.length === 0)
    return entries.length === 0
      ? "nothing connected yet"
      : "everything is switched off";
  const count = (state: ConnectionState) =>
    on.filter((entry) => entry.state === state).length;
  const down = count("down");
  const slow = count("slow");
  const untested = count("untested");
  return [
    `${on.length - down - untested}/${on.length} answering`,
    slow > 0 ? `${slow} slow` : "",
    down > 0 ? `${down} down` : "",
    untested > 0 ? `${untested} not tested` : "",
  ]
    .filter(Boolean)
    .join(" · ");
}

/** What a connection is and where Weir reaches it: "Radarr · http://localhost:7878". */
function whatItIs(entry: ConnectionEntry): string {
  return entry.address
    ? `${entry.kindLabel} · ${entry.address}`
    : entry.kindLabel;
}

/** The row's second line: what it is and where, or why it is not answering. */
export function connectionSub(entry: ConnectionEntry): string {
  if (entry.state === "down") return entry.detail || "not answering";
  if (entry.state === "slow" && entry.answerMs !== null)
    return `${entry.kindLabel} · slow: ${secondsWords(entry.answerMs)}`;
  return whatItIs(entry);
}

/** Everything the row knows: its name, what it is and where, and what its last test said. */
export function connectionTooltip(entry: ConnectionEntry): string {
  return [`${entry.name}: ${whatItIs(entry)}`, entry.detail]
    .filter(Boolean)
    .join(" — ");
}

/** The row's right-hand words: when it was last checked, or that it is being tested or is switched off. */
export function checkedWords(
  entry: ConnectionEntry,
  testing: boolean,
  now: number,
): string {
  if (testing) return "testing…";
  if (entry.state === "off") return "switched off";
  const when = checkedAgo(entry.checkedAt, now);
  return when || "not yet";
}
