/**
 * The answer for a path no route handles by hand: the contract's own empty answer, with a write's fields echoed back.
 * A path the contract does not describe at all is not answered.
 */
import { resolve, findOperation, successResponse } from "../openapi/spec.mjs";
import { skeleton } from "../openapi/skeleton.mjs";
import { Reply } from "./reply.mjs";

/** A write's body fields that its answer also names, laid over the empty answer. */
function echoed(base, schema, body) {
  const properties = resolve(schema)?.properties ?? {};
  const echoes = Object.fromEntries(
    Object.entries(body).filter(
      ([name]) => name in properties && name !== "csrf_token",
    ),
  );
  return { ...base, ...echoes };
}

/**
 * @param {string} method
 * @param {string} pathname
 * @param {Record<string, any>} body
 * @returns {Reply | null} Null when the contract has no such operation.
 */
export function answerFromContract(method, pathname, body) {
  const found = findOperation(method, pathname);
  if (!found) return null;
  const { status, schema } = successResponse(found);
  if (status === 204 || !schema)
    return new Reply(status, status === 204 ? undefined : {});
  const empty = skeleton(schema);
  const isRecord =
    empty !== null && typeof empty === "object" && !Array.isArray(empty);
  return new Reply(status, isRecord ? echoed(empty, schema, body) : empty);
}
