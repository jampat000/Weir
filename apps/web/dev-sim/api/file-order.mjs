/** How the file list is ordered and paged: the sorts the server offers, and what a cursor holds for each. */
import { DIRECTION, KIND, decodeCursor } from "./keyset.mjs";
import {
  directionParam,
  literalParam,
  unknownCursorIssue,
} from "./validation.mjs";

export const FILE_SORT = Object.freeze({
  FILE: "file",
  STATUS: "status",
  WHEN: "when",
});

/** What a list with no sort is ordered by, and what a cursor made for it calls it. */
const UNSORTED = "last_seen";

/** What a status means to a person, in the order a list sorted by meaning shows them (the server's ProcessingFileMeanings). */
const MEANING_ORDER = ["done", "todo", "doing", "attention", "broken", "idle"];

const MEANING_OF_STATUS = Object.freeze({
  processed: "done",
  unprocessed: "todo",
  out_of_schedule: "todo",
  processing: "doing",
  on_hold: "attention",
  blocked_upstream: "todo",
  passed_through: "attention",
  rejected: "attention",
  processing_failed: "broken",
  skipped: "idle",
  disabled: "idle",
  cancelled: "idle",
});

/** A status with no meaning follows every one that has. */
const meaningRank = (status) => {
  const rank = MEANING_ORDER.indexOf(MEANING_OF_STATUS[status]);
  return rank < 0 ? MEANING_ORDER.length : rank;
};

/**
 * Whether the file's cleaned copy still waits for a media manager to take it: the workflow is linked to one, no
 * manager has answered about the copy, and Weir has not settled it (the web's awaitsImport and the server's rule).
 */
export const awaitsImport = (file, linked) =>
  linked &&
  file.handback != null &&
  !file.handback.outcome &&
  !(file.handback.settled_at && file.handback.release_note);

/** A copy waiting for its media manager is to do, whatever the file's status. */
const sortRank = (file, linked) =>
  awaitsImport(file, linked)
    ? MEANING_ORDER.indexOf("todo")
    : meaningRank(file.status);

const byTime = {
  kinds: [KIND.NUMBER, KIND.NUMBER],
  keyOf: (file) => [file.updatedAt, file.id],
};

const SORTS = {
  [FILE_SORT.FILE]: {
    kinds: [KIND.TEXT_IGNORING_CASE, KIND.NUMBER],
    keyOf: (file) => [file.relativePath, file.id],
  },
  [FILE_SORT.STATUS]: {
    kinds: [KIND.NUMBER, KIND.TEXT, KIND.NUMBER],
    keyOf: (file, linked) => [sortRank(file, linked), file.status, file.id],
  },
  [FILE_SORT.WHEN]: byTime,
  [UNSORTED]: byTime,
};

/**
 * The order a request asks for: its sort, direction and the key of the file a cursor says to start after. A sort,
 * direction or cursor the server would refuse is added to `issues`.
 * @param {URLSearchParams} query @param {object[]} issues
 * @param {(file: object) => boolean} isLinked Whether the file's workflow is linked to a media manager.
 */
export function readFileOrder(query, issues, isLinked) {
  const asked = literalParam(query, "sort", Object.values(FILE_SORT), issues);
  const askedDirection = directionParam(query, issues);
  const sort = asked ?? UNSORTED;
  const direction = askedDirection ?? DIRECTION.DESC;
  const { kinds, keyOf } = SORTS[sort];
  const parts = kinds.map((kind) => ({ kind, direction }));
  const order = {
    sort,
    direction,
    parts,
    keyOf: (file) => keyOf(file, isLinked(file)),
    after: null,
  };
  const cursor = query.get("cursor");
  const askedValidly =
    (query.get("sort") === null || asked !== null) &&
    (query.get("direction") === null || askedDirection !== null);
  if (!cursor || !askedValidly) return order;
  order.after = decodeCursor(cursor, sort, direction, parts);
  if (order.after === null) issues.push(unknownCursorIssue(cursor, "list"));
  return order;
}
