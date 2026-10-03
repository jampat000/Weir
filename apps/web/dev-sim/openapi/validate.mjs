/**
 * Checks a value against a contract schema and says what is wrong in plain words, so a test can prove every answer the
 * simulation gives has the shape the web app was generated against.
 */
import { resolve } from "./spec.mjs";

function typeName(value) {
  if (value === null) return "null";
  if (Array.isArray(value)) return "array";
  return Number.isInteger(value) ? "integer" : typeof value;
}

function matchesType(type, value) {
  const actual = typeName(value);
  return actual === type || (type === "number" && actual === "integer");
}

function violationsOfVariants(variants, value, at) {
  const attempts = variants.map((variant) => violations(variant, value, at));
  return attempts.some((found) => found.length === 0)
    ? []
    : (attempts[0] ?? []);
}

function violationsOfObject(schema, value, at) {
  const found = [];
  for (const name of schema.required ?? []) {
    if (!(name in value)) found.push(`${at}.${name} is missing`);
  }
  for (const [name, child] of Object.entries(schema.properties ?? {})) {
    if (name in value)
      found.push(...violations(child, value[name], `${at}.${name}`));
  }
  return found;
}

/**
 * @param {Record<string, any>} schema
 * @param {unknown} value
 * @param {string} [at] Where the value sits, for the message.
 * @returns {string[]}
 */
export function violations(schema, value, at = "$") {
  const resolved = resolve(schema);
  if (!resolved) return [];
  if (resolved.allOf)
    return resolved.allOf.flatMap((part) => violations(part, value, at));
  const variants = resolved.anyOf ?? resolved.oneOf;
  if (variants) return violationsOfVariants(variants, value, at);
  if (resolved.enum)
    return resolved.enum.includes(value)
      ? []
      : [
          `${at} is ${JSON.stringify(value)}, not one of ${resolved.enum.join(", ")}`,
        ];
  const types = [resolved.type].flat().filter(Boolean);
  if (types.length > 0 && !types.some((type) => matchesType(type, value))) {
    return [`${at} is ${typeName(value)}, expected ${types.join(" or ")}`];
  }
  if (Array.isArray(value) && resolved.items) {
    return value.flatMap((item, index) =>
      violations(resolved.items, item, `${at}[${index}]`),
    );
  }
  if (value !== null && typeof value === "object" && !Array.isArray(value)) {
    return violationsOfObject(resolved, value, at);
  }
  return [];
}
