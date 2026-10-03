/**
 * A valid, empty-looking instance of any contract schema: the base the simulation's hand-written answers are laid
 * over, so a field the web app reads is never missing because a response was typed out by hand.
 */
import { resolve, schemaNamed } from "./spec.mjs";
import { toWire } from "../wire-time.mjs";

const MAX_DEPTH = 8;

/** The first variant that is not the null type, or null when null is the only one. */
function firstConcrete(variants) {
  return variants.find((variant) => variant.type !== "null") ?? null;
}

function skeletonOfType(schema, depth, nowMs) {
  const type = Array.isArray(schema.type)
    ? schema.type.find((t) => t !== "null")
    : schema.type;
  switch (type) {
    case "object":
      return skeletonOfObject(schema, depth, nowMs);
    case "array":
      return [];
    case "boolean":
      return false;
    case "integer":
    case "number":
      return schema.minimum ?? 0;
    case "string":
      return schema.format === "date-time" ? toWire(nowMs) : "";
    default:
      return schema.properties ? skeletonOfObject(schema, depth, nowMs) : null;
  }
}

function skeletonOfObject(schema, depth, nowMs) {
  const result = {};
  for (const name of schema.required ?? []) {
    result[name] = skeleton(schema.properties?.[name] ?? {}, depth + 1, nowMs);
  }
  return result;
}

/**
 * @param {Record<string, any>} schema
 * @param {number} [depth]
 * @param {number} [nowMs] The time a date-time field reads as.
 * @returns {unknown}
 */
export function skeleton(schema, depth = 0, nowMs = Date.now()) {
  const resolved = resolve(schema);
  if (!resolved || depth > MAX_DEPTH) return null;
  if (resolved.enum) return resolved.enum[0];
  if (resolved.allOf) {
    return Object.assign(
      {},
      ...resolved.allOf.map((part) => skeleton(part, depth + 1, nowMs)),
    );
  }
  const variants = resolved.anyOf ?? resolved.oneOf;
  if (variants) {
    const resolvedVariants = variants.map(resolve);
    if (resolvedVariants.some((variant) => variant.type === "null"))
      return null;
    const concrete = firstConcrete(resolvedVariants);
    return concrete ? skeleton(concrete, depth + 1, nowMs) : null;
  }
  return skeletonOfType(resolved, depth, nowMs);
}

/**
 * A named contract schema's skeleton with the given fields laid over it.
 * @param {string} schemaName A component schema, such as `ProcessingLibraryOut`.
 * @param {Record<string, unknown>} [fields]
 */
export function shaped(schemaName, fields = {}) {
  if (!skeletons.has(schemaName)) {
    if (!schemaNamed(schemaName))
      throw new Error(`The API contract has no schema named ${schemaName}.`);
    skeletons.set(
      schemaName,
      skeleton({ $ref: `#/components/schemas/${schemaName}` }),
    );
  }
  return { ...structuredClone(skeletons.get(schemaName)), ...fields };
}

/** Skeletons built so far, by schema name: a list of eighty files would otherwise walk the schema eighty times. */
const skeletons = new Map();
