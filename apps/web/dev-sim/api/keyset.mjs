/**
 * Sorting a list and paging it by position, as the server does. A row's place is its key: the values of the sort's own
 * parts, ending in one that is unique to the row, so two rows never share a place and a page can start right after any
 * row. A cursor holds the key of the last row of a page, with the sort and direction it was made under.
 */

/** What the values of one part of a key are, and so how two of them compare. */
export const KIND = Object.freeze({
  NUMBER: "number",
  TEXT: "text",
  TEXT_IGNORING_CASE: "text_ignoring_case",
});

export const DIRECTION = Object.freeze({ ASC: "asc", DESC: "desc" });

/** The longest cursor read: a key is a few short values, so anything longer was not made here. */
const MAX_CURSOR_LENGTH = 2048;

/** Text compares by its UTF-8 bytes, with the letters A to Z read as a to z when case is ignored. */
const utf8 = (text, ignoreCase) =>
  Buffer.from(
    ignoreCase
      ? text.replace(/[A-Z]/g, (letter) => letter.toLowerCase())
      : text,
  );

function compareValues(a, b, kind) {
  if (a === null || b === null)
    return (a === null ? 0 : 1) - (b === null ? 0 : 1);
  if (kind === KIND.NUMBER) return Math.sign(a - b);
  return Buffer.compare(
    utf8(a, kind === KIND.TEXT_IGNORING_CASE),
    utf8(b, kind === KIND.TEXT_IGNORING_CASE),
  );
}

/**
 * Negative when key `a` comes before key `b` in the order the parts give, positive when after. A part with no value
 * (null) sorts before every value.
 * @param {unknown[]} a @param {unknown[]} b @param {{ kind: string, direction: string }[]} parts
 */
export function compareKeys(a, b, parts) {
  for (const [index, part] of parts.entries()) {
    const byValue = compareValues(a[index], b[index], part.kind);
    if (byValue !== 0)
      return part.direction === DIRECTION.DESC ? -byValue : byValue;
  }
  return 0;
}

/** @param {string} sort @param {string} direction @param {unknown[]} key */
export const encodeCursor = (sort, direction, key) =>
  Buffer.from(JSON.stringify({ sort, direction, after: key })).toString(
    "base64url",
  );

const fits = (value, kind) =>
  value === null ||
  (kind === KIND.NUMBER ? Number.isFinite(value) : typeof value === "string");

/**
 * The key a cursor stands for, or null when it is not one made for this sort and direction.
 * @param {string | null} text @param {string} sort @param {string} direction @param {{ kind: string }[]} parts
 */
export function decodeCursor(text, sort, direction, parts) {
  if (!text || text.length > MAX_CURSOR_LENGTH) return null;
  let wire;
  try {
    wire = JSON.parse(Buffer.from(text, "base64url").toString());
  } catch {
    return null;
  }
  const readable =
    wire?.sort === sort &&
    wire.direction === direction &&
    Array.isArray(wire.after) &&
    wire.after.length === parts.length &&
    wire.after.every((value, index) => fits(value, parts[index].kind));
  return readable ? wire.after : null;
}

/**
 * One page of `rows` in the order of `parts`, starting after the row whose key is `after`, and the cursor of the next
 * page when rows remain.
 * @param {object[]} rows
 * @param {object} options
 * @param {{ kind: string, direction: string }[]} options.parts
 * @param {(row: object) => unknown[]} options.keyOf
 * @param {string} options.sort The name a cursor made here carries.
 * @param {string} options.direction
 * @param {unknown[] | null} options.after
 * @param {number} options.limit
 */
export function pageOf(rows, { parts, keyOf, sort, direction, after, limit }) {
  const ordered = rows
    .map((row) => ({ row, key: keyOf(row) }))
    .sort((a, b) => compareKeys(a.key, b.key, parts));
  const remaining = after
    ? ordered.filter((entry) => compareKeys(entry.key, after, parts) > 0)
    : ordered;
  const shown = remaining.slice(0, limit);
  return {
    rows: shown.map((entry) => entry.row),
    nextCursor:
      remaining.length > shown.length
        ? encodeCursor(sort, direction, shown.at(-1).key)
        : null,
  };
}
