/** A list of records a person can add to, change and delete, kept in memory: the shape most of Settings has. */
import { knownFields } from "../openapi/fields.mjs";
import { findOperation, successResponse } from "../openapi/spec.mjs";
import { shaped } from "../openapi/skeleton.mjs";
import { nextId } from "../store.mjs";
import { toWire } from "../wire-time.mjs";
import { noContent, notFound, Reply } from "./reply.mjs";

/**
 * Registers list, read, create, change and delete routes for one kind of record.
 * @param {import("./router.mjs").Router} router
 * @param {object} options
 * @param {string} options.path The collection's path, such as `/api/v1/processing/rule-sets`.
 * @param {string} options.schemaName The record's schema.
 * @param {(sim: import("../sim.mjs").Sim) => Record<string, any>[]} options.records The list the routes keep.
 * @param {string} options.idParam The path parameter that names a record.
 * @param {(record: Record<string, any>, sim: import("../sim.mjs").Sim) => Record<string, any>} [options.present] How a record is shown.
 * @param {(record: Record<string, any>, records: Record<string, any>[]) => Record<string, any>} [options.onCreate] Fields a new record starts with.
 * @param {() => Record<string, any>} [options.defaults] What a new record starts with before the saved fields are laid over it.
 * @param {boolean} [options.readsOne] Whether a single record can be read by its id.
 */
export function registerCollection(
  router,
  {
    path,
    schemaName,
    records,
    idParam,
    present = (record) => record,
    onCreate = () => ({}),
    defaults = () => ({}),
    readsOne = true,
  },
) {
  const createdStatus = successResponse(findOperation("POST", path)).status;
  const find = (sim, params) =>
    records(sim).find((record) => record.id === Number(params[idParam]));

  router.get(path, ({ sim }) =>
    records(sim).map((record) => present(record, sim)),
  );
  if (readsOne) {
    router.get(`${path}/:${idParam}`, ({ sim, params }) => {
      const record = find(sim, params);
      return record
        ? present(record, sim)
        : notFound("That record does not exist.");
    });
  }
  router.post(path, ({ sim, body }) => {
    const list = records(sim);
    const created = shaped(schemaName, {
      ...defaults(),
      ...knownFields(schemaName, body),
      id: nextId(list),
    });
    Object.assign(created, onCreate(created, list));
    if (created.updated_at !== undefined)
      created.updated_at = toWire(sim.now());
    list.push(created);
    return new Reply(createdStatus, present(created, sim));
  });
  router.put(`${path}/:${idParam}`, ({ sim, params, body }) => {
    const record = find(sim, params);
    if (!record) return notFound("That record does not exist.");
    Object.assign(record, knownFields(schemaName, body));
    if (record.updated_at !== undefined) record.updated_at = toWire(sim.now());
    return present(record, sim);
  });
  router.delete(`${path}/:${idParam}`, ({ sim, params }) => {
    const list = records(sim);
    const index = list.findIndex(
      (record) => record.id === Number(params[idParam]),
    );
    if (index < 0) return notFound("That record does not exist.");
    list.splice(index, 1);
    return noContent();
  });
}
