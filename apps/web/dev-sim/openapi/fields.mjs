/** Reads a write's JSON body against the schema its answer has, so a save is echoed back field for field. */
import { resolve, schemaNamed } from "./spec.mjs";

/** The write's secrets are never kept; the answer only says whether one is saved. */
const SECRET_FLAGS = {
  api_key: "api_key_is_saved",
  password: "password_is_saved",
};

function allowsNull(schema) {
  const resolved = resolve(schema);
  if (!resolved) return true;
  if ([resolved.type].flat().includes("null")) return true;
  return (resolved.anyOf ?? resolved.oneOf ?? []).some(allowsNull);
}

/**
 * The part of a request body that belongs on the record the schema describes. A `null` means "leave as it is" unless
 * the record's own field can be null.
 * @param {string} schemaName The record's schema, such as `ProcessingLibraryOut`.
 * @param {Record<string, any>} body
 * @returns {Record<string, unknown>}
 */
export function knownFields(schemaName, body) {
  const properties = schemaNamed(schemaName)?.properties ?? {};
  const fields = {};
  for (const [name, value] of Object.entries(body)) {
    if (name in SECRET_FLAGS) {
      if (typeof value === "string" && value !== "")
        fields[SECRET_FLAGS[name]] = true;
      continue;
    }
    if (!(name in properties) || value === undefined) continue;
    if (value === null && !allowsNull(properties[name])) continue;
    fields[name] = value;
  }
  return fields;
}
