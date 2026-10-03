/** A settings record there is only one of: read it, save it, read it back. */
import { knownFields } from "../openapi/fields.mjs";
import { toWire } from "../wire-time.mjs";

/**
 * @param {import("./router.mjs").Router} router
 * @param {object} options
 * @param {string} options.path
 * @param {string} options.schemaName The record's schema, for reading a save's body.
 * @param {(sim: import("../sim.mjs").Sim) => Record<string, any>} options.record
 * @param {(saved: Record<string, any>, sim: import("../sim.mjs").Sim) => void} [options.afterSave]
 */
export function registerSingleton(
  router,
  { path, schemaName, record, afterSave = () => {} },
) {
  router.get(path, ({ sim }) => record(sim));
  router.put(path, ({ sim, body }) => {
    const saved = record(sim);
    Object.assign(saved, knownFields(schemaName, body));
    if (saved.updated_at !== undefined) saved.updated_at = toWire(sim.now());
    afterSave(saved, sim);
    return saved;
  });
}
