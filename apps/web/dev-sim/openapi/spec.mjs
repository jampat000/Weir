/**
 * The API contract the web app is generated from (apps/web/openapi/weir-openapi.json), indexed so the simulation can
 * answer in the real response shapes and tell which paths the real server has.
 */
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const SPEC_PATH = fileURLToPath(
  new URL("../../openapi/weir-openapi.json", import.meta.url),
);
const HTTP_METHODS = ["get", "post", "put", "patch", "delete"];

/** @typedef {Record<string, any>} Schema */
/** @typedef {{ method: string, template: string, pattern: RegExp, keys: string[], operation: Record<string, any> }} Operation */

/** @type {{ components: { schemas: Record<string, Schema> }, paths: Record<string, Record<string, any>> } | null} */
let cachedSpec = null;
/** @type {Operation[] | null} */
let cachedOperations = null;

export function spec() {
  cachedSpec ??= JSON.parse(readFileSync(SPEC_PATH, "utf8"));
  return cachedSpec;
}

/** A `{name}` path template as a matcher that captures each parameter. */
function templateMatcher(template) {
  const keys = [];
  const source = template
    .split("/")
    .map((part) => {
      const name = /^\{(.+)\}$/.exec(part)?.[1];
      if (!name) return part.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
      keys.push(name);
      return "([^/]+)";
    })
    .join("/");
  return { pattern: new RegExp(`^${source}$`), keys };
}

/** Every operation the contract describes. Literal path segments win over parameters when both match. */
export function operations() {
  if (cachedOperations) return cachedOperations;
  const all = [];
  for (const [template, item] of Object.entries(spec().paths)) {
    const { pattern, keys } = templateMatcher(template);
    for (const method of HTTP_METHODS) {
      if (item[method])
        all.push({
          method: method.toUpperCase(),
          template,
          pattern,
          keys,
          operation: item[method],
        });
    }
  }
  cachedOperations = all.sort((a, b) => a.keys.length - b.keys.length);
  return cachedOperations;
}

/**
 * The contract's operation for a request, or null for a path it does not describe.
 * @param {string} method
 * @param {string} pathname
 * @returns {Operation | null}
 */
export function findOperation(method, pathname) {
  return (
    operations().find(
      (op) => op.method === method && op.pattern.test(pathname),
    ) ?? null
  );
}

/** @param {Schema} schema */
export function resolve(schema) {
  let current = schema;
  while (current?.$ref) {
    current = spec().components.schemas[current.$ref.split("/").pop()];
  }
  return current;
}

/** A named component schema, resolved. */
export function schemaNamed(name) {
  return resolve({ $ref: `#/components/schemas/${name}` });
}

/**
 * The schema of an operation's success response, and the status it answers with.
 * @param {Operation} found
 * @returns {{ status: number, schema: Schema | null }}
 */
export function successResponse(found) {
  const responses = found.operation.responses ?? {};
  const status =
    ["200", "201", "202", "204"].find((code) => responses[code]) ?? "200";
  const schema =
    responses[status]?.content?.["application/json"]?.schema ?? null;
  return { status: Number(status), schema };
}
