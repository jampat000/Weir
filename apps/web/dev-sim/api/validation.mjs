/** Refusing a query the server would refuse, in the shape it answers: 422 and a list of what was wrong and where. */
import { DIRECTION } from "./keyset.mjs";
import { Reply } from "./reply.mjs";

const UNPROCESSABLE = 422;

/** `'a'`, `'a' or 'b'`, `'a', 'b' or 'c'`. @param {string[]} allowed */
function quotedChoices(allowed) {
  const quoted = allowed.map((item) => `'${item}'`);
  return quoted.length === 1
    ? quoted[0]
    : `${quoted.slice(0, -1).join(", ")} or ${quoted.at(-1)}`;
}

/** @param {string} name @param {string[]} allowed @param {string} input */
const literalIssue = (name, allowed, input) => ({
  type: "literal_error",
  loc: ["query", name],
  msg: `Input should be ${quotedChoices(allowed)}`,
  input,
  ctx: { expected: quotedChoices(allowed) },
});

/**
 * A query value that must be one of `allowed`: the value, or null when the query leaves it out or gives another, which
 * is added to `issues`.
 * @param {URLSearchParams} query @param {string} name @param {string[]} allowed @param {object[]} issues
 */
export function literalParam(query, name, allowed, issues) {
  const raw = query.get(name);
  if (raw === null) return null;
  if (allowed.includes(raw)) return raw;
  issues.push(literalIssue(name, allowed, raw));
  return null;
}

/** The direction a request asks for, or null when it names none. @param {URLSearchParams} query @param {object[]} issues */
export const directionParam = (query, issues) =>
  literalParam(query, "direction", Object.values(DIRECTION), issues);

/** The refusal for a cursor made for another sort or direction, or not made by this list at all. @param {string} cursor @param {string} list */
export const unknownCursorIssue = (cursor, list) => ({
  type: "value_error",
  loc: ["query", "cursor"],
  msg: `Value error, that is not a position this ${list} gave out for this sort`,
  input: cursor,
});

/** @param {object[]} issues */
export const refusal = (issues) => new Reply(UNPROCESSABLE, { detail: issues });
