/** Pause and Resume: the one switch on every screen. */
import { toWire } from "../wire-time.mjs";

const IN_FLIGHT_POLICY =
  "Files already being written finish; nothing new starts until you resume.";

function pauseOut(engine) {
  const { paused, until, scanWhilePaused } = engine.pause;
  return {
    paused,
    paused_until: paused && until !== null ? toWire(until) : null,
    reason: paused ? "Paused from the web app." : "",
    scan_while_paused: scanWhilePaused,
    in_flight_policy: IN_FLIGHT_POLICY,
  };
}

/** @param {import("./router.mjs").Router} router */
export function registerPauseRoutes(router) {
  router.get("/api/v1/pause", ({ sim }) => pauseOut(sim.engine));
  router.put("/api/v1/pause", ({ sim, body }) => {
    sim.engine.setPause(
      {
        paused: body.paused,
        pauseForMinutes: body.pause_for_minutes,
        scanWhilePaused: body.scan_while_paused,
      },
      sim.now(),
    );
    return pauseOut(sim.engine);
  });
}
