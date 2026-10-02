/** How System › Logs is ordered and paged: the sorts the server offers, and the key a cursor holds for each. */
import { DIRECTION, KIND, decodeCursor } from "./keyset.mjs";
import { LEVELS, SOURCE_ORDER } from "./system-log-rules.mjs";
import {
  directionParam,
  literalParam,
  unknownCursorIssue,
} from "./validation.mjs";

export const LOG_SORT = Object.freeze({
  TIME: "time",
  LEVEL: "level",
  SOURCE: "source",
  CATEGORY: "category",
  WORKFLOW: "workflow",
});

/**
 * The parts of a row's key a sort puts first. `names` gives each workflow's name by id. A part that is
 * `alwaysAscending` ignores the direction of the list.
 */
const LEADING_PARTS = {
  [LOG_SORT.TIME]: [],
  [LOG_SORT.LEVEL]: [
    {
      kind: KIND.NUMBER,
      valueOf: (row) => LEVELS.indexOf(row.level),
    },
  ],
  [LOG_SORT.SOURCE]: [{ kind: KIND.TEXT, valueOf: (row) => row.source }],
  [LOG_SORT.CATEGORY]: [{ kind: KIND.TEXT, valueOf: (row) => row.category }],
  [LOG_SORT.WORKFLOW]: [
    {
      kind: KIND.NUMBER,
      alwaysAscending: true,
      valueOf: (row, names) => (names.has(row.workflowId) ? 0 : 1),
    },
    {
      kind: KIND.TEXT_IGNORING_CASE,
      valueOf: (row, names) => names.get(row.workflowId) ?? null,
    },
  ],
};

/** Every sort ends in when the row happened, its source and its number in the source, which no other row shares. */
const TRAILING_PARTS = [
  { kind: KIND.NUMBER, valueOf: (row) => row.atMs },
  { kind: KIND.NUMBER, valueOf: (row) => SOURCE_ORDER[row.source] },
  { kind: KIND.NUMBER, valueOf: (row) => row.key },
];

/**
 * The order a request asks for: its sort, direction and the key of the row a cursor says to start after. A sort,
 * direction or cursor the server would refuse is added to `issues`.
 * @param {URLSearchParams} query @param {Map<number, string>} names @param {object[]} issues
 */
export function readLogOrder(query, names, issues) {
  const asked = literalParam(query, "sort", Object.values(LOG_SORT), issues);
  const askedDirection = directionParam(query, issues);
  const sort = asked ?? LOG_SORT.TIME;
  const direction = askedDirection ?? DIRECTION.DESC;
  const parts = [...LEADING_PARTS[sort], ...TRAILING_PARTS];
  const shape = parts.map((part) => ({
    kind: part.kind,
    direction: part.alwaysAscending ? DIRECTION.ASC : direction,
  }));
  const order = {
    sort,
    direction,
    parts: shape,
    keyOf: (row) => parts.map((part) => part.valueOf(row, names)),
    after: null,
  };
  const cursor = query.get("cursor");
  const askedValidly =
    (query.get("sort") === null || asked !== null) &&
    (query.get("direction") === null || askedDirection !== null);
  if (!cursor || !askedValidly) return order;
  order.after = decodeCursor(cursor, sort, direction, shape);
  if (order.after === null) issues.push(unknownCursorIssue(cursor, "log"));
  return order;
}
