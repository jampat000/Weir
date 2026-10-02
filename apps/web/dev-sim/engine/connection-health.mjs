/**
 * Whether each linked media manager and download client is answering, and how quickly. Weir asks every connection now
 * and then and writes down when it last asked and whether it answered; a scenario can make one stop answering for a
 * while, or answer slowly. Every call Weir makes to a connection, and every call one makes to Weir, goes through here
 * so the stream can say so (connection-activity.mjs).
 */
import { CONNECTED_DETAIL, managerLabel } from "../fixtures/connections.mjs";
import { fromWire, toWire, SECOND_MS } from "../wire-time.mjs";
import { ConnectionActivity } from "./connection-activity.mjs";

/** How often Weir asks each connection whether it is still there, at normal speed. */
const CHECK_EVERY_MS = 60 * SECOND_MS;
/** How long before the session opened a connection that is down from the start last answered. */
const SECONDS_SINCE_CHECK_WHEN_DOWN = 25;
/** How long a call takes, in ms: when the connection answers well, when it answers slowly, and when it gives up. */
const ANSWER_MS = [35, 130];
const SLOW_ANSWER_MS = [2_100, 3_400];
const GIVES_UP_MS = [900, 1_800];

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
  #slowSpells;
  #rng;
  #startedAt;
  #speed;
  /** @type {Map<string, number>} When each connection is next asked, by key. */
  #nextCheckAt = new Map();
  activity = new ConnectionActivity();

  /**
   * @param {object} options
   * @param {import("./engine.mjs").Engine} options.engine
   * @param {import("../scenarios.mjs").Outage[]} options.outages
   * @param {import("../scenarios.mjs").Outage[]} options.slowSpells
   * @param {import("./rng.mjs").Rng} options.rng
   * @param {number} options.startedAt
   * @param {number} options.speed
   */
  constructor({ engine, outages, slowSpells, rng, startedAt, speed }) {
    this.#engine = engine;
    this.#outages = outages;
    this.#slowSpells = slowSpells;
    this.#rng = rng;
    this.#startedAt = startedAt;
    this.#speed = speed;
    for (const { kind, connection } of this.#connections()) {
      if (this.#isDown(kind, connection.id, startedAt))
        this.#record(
          kind,
          connection,
          startedAt - SECONDS_SINCE_CHECK_WHEN_DOWN * SECOND_MS,
          { answered: false, ms: GIVES_UP_MS[0] },
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

  /** Whether one of a scenario's spells has the connection in it at `nowMs`. */
  #isIn(spells, kind, id, nowMs) {
    const sinceStart = (nowMs - this.#startedAt) * this.#speed;
    return spells.some((outage) => {
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

  /** Whether a scenario has the connection not answering at `nowMs`. */
  #isDown(kind, id, nowMs) {
    return this.#isIn(this.#outages, kind, id, nowMs);
  }

  /** How a call to the connection at `nowMs` ends, and how long it takes. */
  #outcomeAt(kind, id, nowMs) {
    if (this.#isDown(kind, id, nowMs))
      return {
        answered: false,
        ms: Math.round(this.#rng.between(...GIVES_UP_MS)),
      };
    const range = this.#isIn(this.#slowSpells, kind, id, nowMs)
      ? SLOW_ANSWER_MS
      : ANSWER_MS;
    return { answered: true, ms: Math.round(this.#rng.between(...range)) };
  }

  /**
   * Weir calls a connection at `nowMs`: the stream says it asked, and says how it ended once the call has taken its time.
   * @param {string} kind One of {@link CONNECTION_KIND}.
   * @param {Record<string, any>} connection
   * @param {number} nowMs
   * @returns {{ answered: boolean, ms: number }}
   */
  reach(kind, connection, nowMs) {
    const outcome = this.#outcomeAt(kind, connection.id, nowMs);
    this.activity.call(kind, connection, nowMs, outcome);
    return outcome;
  }

  /** A connection calls Weir at `nowMs`. */
  receive(kind, connection, nowMs) {
    this.activity.received(kind, connection, nowMs);
  }

  #labelOf(kind, connection) {
    return kind === CONNECTION_KIND.MANAGER
      ? managerLabel(connection)
      : connection.name;
  }

  /** Writes down that Weir asked at `nowMs`, and whether the connection answered and how long it took. */
  #record(kind, connection, nowMs, { answered, ms }) {
    Object.assign(connection, {
      last_answer_ms: ms,
      last_used_at: toWire(nowMs),
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

  /** Asks every connection whose turn has come, and ends the calls that have taken their time. @param {number} nowMs */
  advance(nowMs) {
    this.activity.advance(nowMs);
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
        this.reach(kind, connection, nowMs),
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
    const outcome = this.reach(kind, connection, nowMs);
    this.#record(kind, connection, nowMs, outcome);
    this.#engine.touch();
    return {
      connection_id: connection.id,
      ok: outcome.answered,
      detail: connection.last_test_detail,
      checked_at: connection.last_test_at,
    };
  }
}
