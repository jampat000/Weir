/**
 * What a media manager or download client is doing, as one model for every screen that shows it: its state (answering,
 * slow, not answering, switched off, never tested), when Weir last talked to it and how long that took. The saved
 * connection gives the state as of its last test; an answer pushed on the stream since then is laid over it.
 */
import {
  DOWNLOAD_CLIENT_KIND_LABELS,
  type DownloadClientConnection,
} from "../download-clients/download-clients-api";
import {
  MEDIA_MANAGER_KIND_LABELS,
  type MediaManagerConnection,
} from "../media-managers/media-managers-api";
import { connectionTitle } from "../ui/connection-title";
import { parseAppTime } from "../ui/mm-format-date";
import type { ConnectionKind } from "./connection-activity";

export type ConnectionState = "ok" | "slow" | "down" | "off" | "untested";

/** An answer that took this long or longer is slow. */
export const SLOW_ANSWER_MS = 2000;

export const connectionKey = (kind: ConnectionKind, id: number): string =>
  `${kind}:${id}`;

/** The newest thing a stream frame said about a connection. */
export type ConnectionAnswer = {
  /** When it happened, in ms since the epoch. */
  at: number;
  /** How long the call took, when the frame said. */
  ms: number | null;
  /** Whether Weir's own call got an answer. Null for a connection that called Weir, which says nothing about that. */
  ok: boolean | null;
};

export type ConnectionEntry = {
  key: string;
  kind: ConnectionKind;
  id: number;
  /** The name Weir gives it, then its nickname when it has one. */
  name: string;
  /** The name Weir gives it, from where it runs. */
  baseName: string;
  /** What a person called it, or empty. */
  nickname: string;
  /** Radarr, qBittorrent… */
  kindLabel: string;
  /** Where Weir reaches it. Empty for a manager that only sends to Weir. */
  address: string;
  enabled: boolean;
  state: ConnectionState;
  /** When Weir last talked to it or it last called Weir, in ms since the epoch. */
  checkedAt: number | null;
  /** How long Weir's last call to it took. */
  answerMs: number | null;
  /** What its last test said, when it said anything. */
  detail: string;
  /** Whether a test can reach it: switched on and with an address. */
  testable: boolean;
};

type SavedConnection = {
  id: number;
  name: string;
  nickname?: string | null;
  enabled: boolean;
  base_url: string;
  last_test_ok?: boolean | null;
  last_test_at?: string | null;
  last_test_detail?: string | null;
  last_answer_ms?: number | null;
  last_used_at?: string | null;
};

function stateOf(
  enabled: boolean,
  answered: boolean | null,
  answerMs: number | null,
): ConnectionState {
  if (!enabled) return "off";
  if (answered === null) return "untested";
  if (!answered) return "down";
  return answerMs !== null && answerMs >= SLOW_ANSWER_MS ? "slow" : "ok";
}

function latest(...times: (number | null)[]): number | null {
  const known = times.filter((time): time is number => time !== null);
  return known.length > 0 ? Math.max(...known) : null;
}

function entryOf(
  kind: ConnectionKind,
  connection: SavedConnection,
  kindLabel: string,
  answer: ConnectionAnswer | undefined,
): ConnectionEntry {
  const savedAt = latest(
    parseAppTime(connection.last_test_at),
    parseAppTime(connection.last_used_at),
  );
  const pushed =
    answer && (savedAt === null || answer.at > savedAt) ? answer : null;
  const answerMs = pushed?.ms ?? connection.last_answer_ms ?? null;
  const address = connection.base_url.trim();
  return {
    key: connectionKey(kind, connection.id),
    kind,
    id: connection.id,
    name: connectionTitle(connection),
    baseName: connection.name,
    nickname: connection.nickname?.trim() ?? "",
    kindLabel,
    address,
    enabled: connection.enabled,
    state: stateOf(
      connection.enabled,
      pushed?.ok ?? connection.last_test_ok ?? null,
      answerMs,
    ),
    checkedAt: pushed?.at ?? savedAt,
    answerMs,
    detail: connection.last_test_detail?.trim() ?? "",
    testable: connection.enabled && address !== "",
  };
}

/** Every media manager, then every download client, switched on or not. */
export function connectionEntries(
  managers: readonly MediaManagerConnection[],
  downloadClients: readonly DownloadClientConnection[],
  answers: ReadonlyMap<string, ConnectionAnswer>,
): ConnectionEntry[] {
  return [
    ...managers.map((manager) =>
      entryOf(
        "media_manager",
        manager,
        MEDIA_MANAGER_KIND_LABELS[manager.kind],
        answers.get(connectionKey("media_manager", manager.id)),
      ),
    ),
    ...downloadClients.map((client) =>
      entryOf(
        "download_client",
        client,
        DOWNLOAD_CLIENT_KIND_LABELS[client.kind],
        answers.get(connectionKey("download_client", client.id)),
      ),
    ),
  ];
}

/** How long a call took, in the units a person reads: "84 ms". */
export function answerWords(ms: number): string {
  return `${Math.round(ms).toLocaleString()} ms`;
}

/** Whether a connection is down or slow: something to look at. */
export const needsALook = (entry: ConnectionEntry): boolean =>
  entry.state === "down" || entry.state === "slow";
