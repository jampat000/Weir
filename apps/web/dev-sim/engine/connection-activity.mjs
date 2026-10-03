/**
 * Weir talking to its media managers and download clients, as the server's `connection.activity` frames say it: an
 * outbound call is `asked` when it starts and `answered` or `failed` when it ends, with how long it took; a connection
 * calling Weir is a single inbound `answered`. A call that ends also writes how long it took and when on the connection.
 */
import { fromWire, toWire } from "../wire-time.mjs";

export const PHASE = Object.freeze({
  ASKED: "asked",
  ANSWERED: "answered",
  FAILED: "failed",
});

export const DIRECTION = Object.freeze({
  OUTBOUND: "outbound",
  INBOUND: "inbound",
});

/** The frame's name for each kind of connection the engine keeps. */
const FRAME_KIND = Object.freeze({
  manager: "media_manager",
  client: "download_client",
});

/**
 * @typedef {object} Outcome
 * @property {boolean} answered
 * @property {number} ms How long the call takes, in ms.
 */

export class ConnectionActivity {
  /** @type {Set<(frame: Record<string, unknown>) => void>} */
  #listeners = new Set();
  /** @type {{ endsAt: number, kind: string, connection: Record<string, any>, outcome: Outcome }[]} */
  #inFlight = [];

  /** Tells `listener` about every frame as it is made. @param {(frame: Record<string, unknown>) => void} listener */
  onFrame(listener) {
    this.#listeners.add(listener);
    return () => this.#listeners.delete(listener);
  }

  #emit(kind, connection, phase, direction, nowMs, ms) {
    const frame = {
      kind: FRAME_KIND[kind],
      id: connection.id,
      phase,
      direction,
      at: toWire(nowMs),
      ms,
    };
    for (const listener of this.#listeners) listener(frame);
  }

  /**
   * Weir starts a call to a connection that will end as `outcome` says.
   * @param {string} kind One of the connection kinds.
   * @param {Record<string, any>} connection
   * @param {number} nowMs
   * @param {Outcome} outcome
   */
  call(kind, connection, nowMs, outcome) {
    this.#emit(kind, connection, PHASE.ASKED, DIRECTION.OUTBOUND, nowMs, null);
    this.#inFlight.push({
      endsAt: nowMs + outcome.ms,
      kind,
      connection,
      outcome,
    });
  }

  /** A connection calls Weir: a hand-off, a webhook or an outcome. */
  received(kind, connection, nowMs) {
    this.#emit(
      kind,
      connection,
      PHASE.ANSWERED,
      DIRECTION.INBOUND,
      nowMs,
      null,
    );
    connection.last_used_at = toWire(nowMs);
  }

  /** Ends every call whose time has come. @param {number} nowMs */
  advance(nowMs) {
    const ending = this.#inFlight.filter((call) => call.endsAt <= nowMs);
    if (ending.length === 0) return;
    this.#inFlight = this.#inFlight.filter((call) => call.endsAt > nowMs);
    for (const { kind, connection, outcome, endsAt } of ending) {
      const phase = outcome.answered ? PHASE.ANSWERED : PHASE.FAILED;
      this.#emit(
        kind,
        connection,
        phase,
        DIRECTION.OUTBOUND,
        endsAt,
        outcome.ms,
      );
      connection.last_answer_ms = Math.round(outcome.ms);
      connection.last_used_at = toWire(
        Math.max(endsAt, fromWire(connection.last_used_at) ?? 0),
      );
    }
  }
}
