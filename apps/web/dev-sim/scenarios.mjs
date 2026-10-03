/**
 * The three kinds of day the simulation can show, chosen with SIM_SCENARIO. A scenario sets how much is happening,
 * what waits for a person, and which connections misbehave; the screens read the result like any other Weir.
 */
import { MINUTE_MS, SECOND_MS } from "./wire-time.mjs";
import { FOUR_K_MANAGER_ID, TV_MANAGER_ID } from "./fixtures/connections.mjs";

export const SCENARIO = Object.freeze({
  BUSY: "busy",
  QUIET: "quiet",
  TROUBLE: "trouble",
});

export const DEFAULT_SCENARIO = SCENARIO.BUSY;

/**
 * @typedef {object} Outage
 * @property {{ kind: "manager" | "client", id: number }} connection
 * @property {number} startsAfterMs How long after the session starts the connection stops answering; 0 means it is down from the start.
 * @property {number} lastsMs How long it stays down; Infinity means it never comes back.
 * @property {number | null} repeatsEveryMs The gap between one outage starting and the next, or null for a single one.
 */

/**
 * @typedef {object} Scenario
 * @property {string} name
 * @property {number} pace How busy the folders are: 1 is normal, less spaces new downloads and library cleans further apart.
 * @property {{ files: number, cleansPerWorkflow: number }} history What already finished before the session opened.
 * @property {boolean} workInProgress Whether files are being written, queued and held when the session opens.
 * @property {{ languageRejected: number, failedWhileWriting: number, onHoldStuck: number, pathRule: number }} needsYou Files already waiting on a person.
 * @property {{ rejected: number, failed: number }} arrivalShares The share of new downloads the rules reject, and the share that fail while writing.
 * @property {boolean} fourKManagerSaysWhereItSaves Whether the 4K manager reports where its downloads land, so its folder chain can be verified.
 * @property {Outage[]} outages
 * @property {Outage[]} slowSpells When a connection still answers but takes two seconds or more to; the same shape as an outage.
 */

/** @type {Record<string, Scenario>} */
export const SCENARIOS = {
  [SCENARIO.BUSY]: {
    name: SCENARIO.BUSY,
    pace: 1,
    history: { files: 46, cleansPerWorkflow: 3 },
    workInProgress: true,
    needsYou: {
      languageRejected: 3,
      failedWhileWriting: 1,
      onHoldStuck: 1,
      pathRule: 1,
    },
    arrivalShares: { rejected: 0.05, failed: 0.035 },
    fourKManagerSaysWhereItSaves: false,
    outages: [
      {
        connection: { kind: "manager", id: FOUR_K_MANAGER_ID },
        startsAfterMs: 150 * SECOND_MS,
        lastsMs: 90 * SECOND_MS,
        repeatsEveryMs: 10 * MINUTE_MS,
      },
    ],
    slowSpells: [
      {
        connection: { kind: "manager", id: FOUR_K_MANAGER_ID },
        startsAfterMs: 60 * SECOND_MS,
        lastsMs: 80 * SECOND_MS,
        repeatsEveryMs: 10 * MINUTE_MS,
      },
    ],
  },
  [SCENARIO.QUIET]: {
    name: SCENARIO.QUIET,
    pace: 0.08,
    history: { files: 6, cleansPerWorkflow: 1 },
    workInProgress: false,
    needsYou: {
      languageRejected: 0,
      failedWhileWriting: 0,
      onHoldStuck: 0,
      pathRule: 0,
    },
    arrivalShares: { rejected: 0, failed: 0 },
    fourKManagerSaysWhereItSaves: true,
    outages: [],
    slowSpells: [],
  },
  [SCENARIO.TROUBLE]: {
    name: SCENARIO.TROUBLE,
    pace: 1,
    history: { files: 30, cleansPerWorkflow: 2 },
    workInProgress: true,
    needsYou: {
      languageRejected: 7,
      failedWhileWriting: 3,
      onHoldStuck: 2,
      pathRule: 2,
    },
    arrivalShares: { rejected: 0.14, failed: 0.1 },
    fourKManagerSaysWhereItSaves: false,
    outages: [
      {
        connection: { kind: "manager", id: TV_MANAGER_ID },
        startsAfterMs: 0,
        lastsMs: Infinity,
        repeatsEveryMs: null,
      },
    ],
    slowSpells: [
      {
        connection: { kind: "manager", id: FOUR_K_MANAGER_ID },
        startsAfterMs: 45 * SECOND_MS,
        lastsMs: 120 * SECOND_MS,
        repeatsEveryMs: 8 * MINUTE_MS,
      },
    ],
  },
};

/**
 * The scenario a SIM_SCENARIO value names; nothing set means the default.
 * @param {string | undefined} value
 * @returns {Scenario}
 */
export function scenarioNamed(value) {
  const name = value?.trim().toLowerCase() || DEFAULT_SCENARIO;
  const scenario = SCENARIOS[name];
  if (!scenario)
    throw new Error(
      `SIM_SCENARIO must be one of ${Object.keys(SCENARIOS).join(", ")}; it was "${value}".`,
    );
  return scenario;
}
