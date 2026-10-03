/** System's live facts: the machine's readings, an overview of this Weir, and its periodic tasks. */
import { overviewOut } from "../machine/overview.mjs";
import { statsOut } from "../machine/wire.mjs";

/** @param {import("./router.mjs").Router} router */
export function registerMachineRoutes(router) {
  router.get("/api/v1/system/stats", ({ sim }) =>
    statsOut(sim.machine, sim.now()),
  );
  router.get("/api/v1/system/overview", ({ sim }) => overviewOut(sim));
  router.get("/api/v1/system/tasks", ({ sim }) => sim.tasks.rows());
}
