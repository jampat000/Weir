/** Workflows (the libraries Weir watches) and the rule sets they use: what Settings › Workflows and Rules edit. */
import { STATUS } from "../engine/file.mjs";
import { managerLabel } from "../fixtures/connections.mjs";
import { libraryDefaults, ruleSetDefaults } from "../fixtures/workflows.mjs";
import { toWire } from "../wire-time.mjs";
import { registerCollection } from "./collection.mjs";
import { folderChainOf } from "./folder-chain.mjs";
import { notFound } from "./reply.mjs";

const BUSY_STATUSES = [STATUS.WAITING, STATUS.PROCESSING];
/** Each workflow looks at its folder on its own beat, so the countdowns on Processing do not all end together. */
const SCAN_PHASE_MS = 97_000;

/** Weir's next look at a workflow's folder, on a fixed beat of the workflow's scan interval. */
function nextLookAt(library, nowMs) {
  const interval = library.scan_interval_seconds * 1000;
  const phase = (library.id * SCAN_PHASE_MS) % interval;
  return Math.ceil((nowMs - phase) / interval) * interval + phase;
}

/** Whether the managers a workflow is linked to can say what they are still importing. */
function managerCoverage(library, store) {
  const linked = store.managers.filter(
    (manager) =>
      manager.enabled && library.manager_connection_ids?.includes(manager.id),
  );
  if (linked.length === 0)
    return {
      manager_coverage: "no_upstream_signal",
      manager_coverage_detail:
        "No media manager is linked to this workflow, so Weir cannot tell when a download is still being imported.",
    };
  const silent = linked.find((manager) => manager.last_test_ok === false);
  if (silent)
    return {
      manager_coverage: "unreachable",
      manager_coverage_detail: `${managerLabel(silent)} is not answering, so Weir cannot see what it is still importing.`,
    };
  return {};
}

function presentLibrary(library, sim) {
  const slots = sim.engine.slots();
  const nextLook = toWire(nextLookAt(library, sim.now()));
  return {
    ...library,
    ...managerCoverage(library, sim.store),
    active_job_count: [...sim.engine.files.values()].filter(
      (file) =>
        file.libraryId === library.id && BUSY_STATUSES.includes(file.status),
    ).length,
    effective_max_concurrent_files:
      library.max_concurrent_files > 0
        ? Math.min(library.max_concurrent_files, slots)
        : slots,
    next_look_at: nextLook,
    next_scan_at: nextLook,
  };
}

function presentRuleSet(ruleSet, sim) {
  return {
    ...ruleSet,
    used_by_library_count: sim.store.libraries.filter(
      (library) => library.rule_set_id === ruleSet.id,
    ).length,
  };
}

/** @param {import("./router.mjs").Router} router */
export function registerWorkflowRoutes(router) {
  const libraryPath = "/api/v1/processing/libraries";
  const withLibrary = (handler) => (context) => {
    const library = context.sim.store.libraries.find(
      (candidate) => candidate.id === Number(context.params.id),
    );
    return library
      ? handler(library, context)
      : notFound("That workflow does not exist.");
  };

  router.post(`${libraryPath}/reorder`, ({ sim, body }) => {
    (body.library_ids_in_order ?? []).forEach((id, index) => {
      const library = sim.store.libraries.find(
        (candidate) => candidate.id === id,
      );
      if (library) library.display_order = index + 1;
    });
    sim.store.libraries.sort((a, b) => a.display_order - b.display_order);
    return sim.store.libraries.map((library) => presentLibrary(library, sim));
  });
  router.post(
    `${libraryPath}/:id/unlink`,
    withLibrary((library, { sim }) => {
      Object.assign(library, {
        discovered_from_connection_id: null,
        discovered_library_key: null,
      });
      return presentLibrary(library, sim);
    }),
  );
  router.get(
    `${libraryPath}/:id/folder-chain`,
    withLibrary((library, { sim }) => folderChainOf(library, sim.store)),
  );
  router.get(
    "/api/v1/media-managers/connections/:id/folder-chain",
    ({ sim, params }) =>
      sim.store.libraries
        .filter((library) =>
          library.manager_connection_ids?.includes(Number(params.id)),
        )
        .map((library) => folderChainOf(library, sim.store)),
  );

  registerCollection(router, {
    path: libraryPath,
    schemaName: "ProcessingLibraryOut",
    idParam: "id",
    records: (sim) => sim.store.libraries,
    present: presentLibrary,
    defaults: libraryDefaults,
    onCreate: (_created, list) => ({ display_order: list.length + 1 }),
  });
  registerCollection(router, {
    path: "/api/v1/processing/rule-sets",
    schemaName: "ProcessingRuleSetOut",
    idParam: "rule_set_id",
    records: (sim) => sim.store.ruleSets,
    present: presentRuleSet,
    defaults: ruleSetDefaults,
    readsOne: false,
  });
}
