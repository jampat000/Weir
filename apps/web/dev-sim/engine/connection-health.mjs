/**
 * Whether each linked media manager and download client is answering. Weir asks every connection now and then and
 * writes down when it last asked and whether it answered; a scenario can make one stop answering for a while.
 */
import { CONNECTED_DETAIL, managerLabel } from "../fixtures/connections.mjs";
import { fromWire, toWire, SECOND_MS } from "../wire-time.mjs";

/** How often Weir asks each connection whether it is still there, at normal speed. */
const CHECK_EVERY_MS = 60 * SECOND_MS;
/** How long before the session opened a connection that is down from the start last answered. */
const SECONDS_SINCE_CHECK_WHEN_DOWN = 25;

export const CONNECTION_KIND = Object.freeze({
  MANAGER: "manager",
  CLIENT: "client",
});

/** The sentence every screen uses when Weir cannot reach a connection at all. */
export const unreachableText = (label, baseUrl) =>
  `Weir could not reach ${label} at ${baseUrl}. Check the address is right, and that the app is running and reachable from this machine.`;

const keyOf = (kind, id) => `${kind}:${id}`;

export class ConnectionHealth {
  #engine;
  #outages;
  #startedAt;
  #speed;
  /** @type {Map<string, number>} When each connection is next asked, by key. */
  #nextCheckAt = new Map();

  /**
   * @param {object} options
   * @param {import("./engine.mjs").Engine} options.engine
   * @param {import("../scenarios.mjs").Outage[]} options.outages
   * @param {number} options.startedAt
   * @param {number} options.speed
   */
  constructor({ engine, outages, startedAt, speed }) {
    this.#engine = engine;
    this.#outages = outages;
    this.#startedAt = startedAt;
    this.#speed = speed;
    for (const { kind, connection } of this.#connections()) {
      if (this.#isDown(kind, connection.id, startedAt))
        this.#record(
          kind,
          connection,
          startedAt - SECONDS_SINCE_CHECK_WHEN_DOWN * SECOND_MS,
          false,
        );
    }
  }

  #connections() {
    const { managers, downloadClients } = this.#engine.store;
    return [
      ...managers.map((connection) => ({
        kind: CONNECTION_KIND.MANAGER,
        connection,
      })),
      ...downloadClients.map((connection) => ({
        kind: CONNECTION_KIND.CLIENT,
        connection,
      })),
    ];
  }

  /** Whether a scenario has the connection not answering at `nowMs`. */
  #isDown(kind, id, nowMs) {
    const sinceStart = (nowMs - this.#startedAt) * this.#speed;
    return this.#outages.some((outage) => {
      if (outage.connection.kind !== kind || outage.connection.id !== id)
        return false;
      const sinceOutage = sinceStart - outage.startsAfterMs;
      if (sinceOutage < 0) return false;
      const sinceThisOutageBegan =
        outage.repeatsEveryMs === null
          ? sinceOutage
          : sinceOutage % outage.repeatsEveryMs;
      return sinceThisOutageBegan < outage.lastsMs;
    });
  }

  #labelOf(kind, connection) {
    return kind === CONNECTION_KIND.MANAGER
      ? managerLabel(connection)
      : connection.name;
  }

  /** Writes down that Weir asked at `nowMs`, and whether the connection answered. */
  #record(kind, connection, nowMs, answered) {
    Object.assign(connection, {
      last_test_ok: answered,
      last_test_at: toWire(nowMs),
      last_test_detail: answered
        ? CONNECTED_DETAIL
        : unreachableText(this.#labelOf(kind, connection), connection.base_url),
    });
    this.#nextCheckAt.set(
      keyOf(kind, connection.id),
      nowMs + CHECK_EVERY_MS / this.#speed,
    );
  }

  /** Asks every connection whose turn has come. @param {number} nowMs */
  advance(nowMs) {
    for (const { kind, connection } of this.#connections()) {
      const key = keyOf(kind, connection.id);
      if (!this.#nextCheckAt.has(key)) {
        const lastAsked = fromWire(connection.last_test_at) ?? nowMs;
        this.#nextCheckAt.set(key, lastAsked + CHECK_EVERY_MS / this.#speed);
      }
      if (nowMs < this.#nextCheckAt.get(key)) continue;
      this.#record(
        kind,
        connection,
        nowMs,
        !this.#isDown(kind, connection.id, nowMs),
      );
      this.#engine.touch();
    }
  }

  /**
   * A person asks Weir to test a connection now.
   * @param {string} kind One of {@link CONNECTION_KIND}.
   * @param {Record<string, any>} connection
   * @param {number} nowMs
   */
  test(kind, connection, nowMs) {
    const answered = !this.#isDown(kind, connection.id, nowMs);
    this.#record(kind, connection, nowMs, answered);
    this.#engine.touch();
    return {
      connection_id: connection.id,
      ok: answered,
      detail: connection.last_test_detail,
      checked_at: connection.last_test_at,
    };
  }
}
