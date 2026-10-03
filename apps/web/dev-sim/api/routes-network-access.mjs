/**
 * Who can reach Weir over the network, as the Windows package answers it: a choice is saved at once, the tray then
 * asks Windows for permission on the PC itself and restarts the server, and the page sees the choice pending until
 * then. The trouble scenario declines the first Windows prompt, so the blocked state can be seen; asking again works.
 */
import { shaped } from "../openapi/skeleton.mjs";
import { SCENARIO } from "../scenarios.mjs";
import { SECOND_MS } from "../wire-time.mjs";
import { MACHINE_NAME } from "../fixtures/connections.mjs";
import { Reply } from "./reply.mjs";

const THIS_PC_ONLY = "this_pc_only";
const NETWORK = "network";

/** How long the person takes to answer Windows' prompt, and the tray to restart the server. */
const APPROVAL_DELAY_MS = 4 * SECOND_MS;

const PORT = 9347;
const LAN_ADDRESS = `http://10.0.0.196:${PORT}`;

/** What the server says for each situation, in the same words. */
const SUMMARY = {
  thisPcOnly: "Only this PC can reach Weir.",
  allowed: "Other devices on your network can reach Weir.",
  blocked:
    "Windows Firewall is blocking other devices from reaching Weir. Try again to ask Windows to allow it, or limit Weir to this PC.",
  restartingForThisPc: "Restarting Weir so only this PC can reach it.",
  restartingForNetwork:
    "Restarting Weir so other devices on your network can reach it.",
  waiting: `Waiting for you to approve Windows Firewall on ${MACHINE_NAME}. Weir restarts for your network once you do.`,
};

/**
 * The session's network access, with any change that has waited long enough carried out first.
 * @param {import("../sim.mjs").Sim} sim
 */
function access(sim) {
  sim.store.networkAccess ??= {
    scope: THIS_PC_ONLY,
    firewallAllows: false,
    asked: null,
    declinesLeft: sim.scenario.name === SCENARIO.TROUBLE ? 1 : 0,
  };
  const record = sim.store.networkAccess;
  if (record.asked && sim.now() - record.asked.at >= APPROVAL_DELAY_MS) {
    if (record.asked.scope === NETWORK && !record.firewallAllows) {
      if (record.declinesLeft > 0) record.declinesLeft -= 1;
      else record.firewallAllows = true;
    }
    record.scope = record.asked.scope;
    record.asked = null;
  }
  return record;
}

function summaryOf(record, pending) {
  if (pending === THIS_PC_ONLY) return SUMMARY.restartingForThisPc;
  if (pending === NETWORK)
    return record.firewallAllows
      ? SUMMARY.restartingForNetwork
      : SUMMARY.waiting;
  if (record.scope === THIS_PC_ONLY) return SUMMARY.thisPcOnly;
  return record.firewallAllows ? SUMMARY.allowed : SUMMARY.blocked;
}

/** @param {import("../sim.mjs").Sim} sim */
function status(sim) {
  const record = access(sim);
  const pending =
    record.asked && record.asked.scope !== record.scope
      ? record.asked.scope
      : null;
  const wantsNetwork = record.scope === NETWORK || pending === NETWORK;
  const firewall = !wantsNetwork
    ? "not_checked"
    : record.firewallAllows
      ? "allowed"
      : "blocked";
  const state =
    record.scope === THIS_PC_ONLY
      ? THIS_PC_ONLY
      : record.firewallAllows
        ? "allowed"
        : "blocked";
  return shaped("SuiteNetworkAccessOut", {
    state,
    summary: summaryOf(record, pending),
    scope: record.scope,
    pending_scope: pending,
    firewall,
    port: PORT,
    machine_name: MACHINE_NAME,
    addresses: wantsNetwork ? [LAN_ADDRESS] : [],
  });
}

/** @param {import("./router.mjs").Router} router */
export function registerNetworkAccessRoutes(router) {
  router.get("/api/v1/suite/network-access", ({ sim }) => status(sim));
  router.put("/api/v1/suite/network-access", ({ sim, body }) => {
    if (body.scope !== THIS_PC_ONLY && body.scope !== NETWORK)
      return new Reply(422, {
        detail: "scope must be this_pc_only or network.",
      });
    const record = access(sim);
    record.asked = { scope: body.scope, at: sim.now() };
    return status(sim);
  });
}
