// @vitest-environment node
import { describe, expect, it } from "vitest";

import {
  DIRECTION,
  KIND,
  compareKeys,
  decodeCursor,
  encodeCursor,
  pageOf,
} from "./keyset.mjs";

const parts = (kind, direction) => [
  { kind, direction },
  { kind: KIND.NUMBER, direction },
];

describe("comparing keys", () => {
  it("compares numbers by size and lets a later part break a tie", () => {
    const ascending = parts(KIND.NUMBER, DIRECTION.ASC);

    expect(compareKeys([9, 1], [10, 1], ascending)).toBeLessThan(0);
    expect(compareKeys([5, 1], [5, 2], ascending)).toBeLessThan(0);
    expect(compareKeys([5, 2], [5, 2], ascending)).toBe(0);
  });

  it("reverses every part when descending", () => {
    const descending = parts(KIND.TEXT, DIRECTION.DESC);

    expect(compareKeys(["b", 1], ["a", 9], descending)).toBeLessThan(0);
    expect(compareKeys(["a", 9], ["a", 1], descending)).toBeLessThan(0);
  });

  it("puts capitals before lowercase unless it ignores case", () => {
    expect(
      compareKeys(["Zeta", 1], ["alpha", 1], parts(KIND.TEXT, DIRECTION.ASC)),
    ).toBeLessThan(0);
    expect(
      compareKeys(
        ["Zeta", 1],
        ["alpha", 1],
        parts(KIND.TEXT_IGNORING_CASE, DIRECTION.ASC),
      ),
    ).toBeGreaterThan(0);
  });

  it("folds only the letters a to z when it ignores case", () => {
    expect(
      compareKeys(
        ["É", 1],
        ["é", 1],
        parts(KIND.TEXT_IGNORING_CASE, DIRECTION.ASC),
      ),
    ).toBeLessThan(0);
  });

  it("puts a missing value before every value when ascending", () => {
    const ascending = parts(KIND.TEXT, DIRECTION.ASC);

    expect(compareKeys([null, 1], ["a", 1], ascending)).toBeLessThan(0);
    expect(
      compareKeys([null, 1], ["a", 1], parts(KIND.TEXT, DIRECTION.DESC)),
    ).toBeGreaterThan(0);
  });
});

describe("a cursor", () => {
  const shape = parts(KIND.TEXT, DIRECTION.DESC);

  it("comes back as the key it was made from", () => {
    const cursor = encodeCursor("file", DIRECTION.DESC, ["Amélie", 7]);

    expect(decodeCursor(cursor, "file", DIRECTION.DESC, shape)).toEqual([
      "Amélie",
      7,
    ]);
  });

  it("is refused for another sort or direction, and when it is not one", () => {
    const cursor = encodeCursor("file", DIRECTION.DESC, ["a", 1]);

    expect(decodeCursor(cursor, "when", DIRECTION.DESC, shape)).toBeNull();
    expect(decodeCursor(cursor, "file", DIRECTION.ASC, shape)).toBeNull();
    expect(decodeCursor("nonsense", "file", DIRECTION.DESC, shape)).toBeNull();
    expect(decodeCursor("", "file", DIRECTION.DESC, shape)).toBeNull();
    expect(decodeCursor(null, "file", DIRECTION.DESC, shape)).toBeNull();
  });

  it("is refused when its values do not fit the key", () => {
    const wrongType = encodeCursor("file", DIRECTION.DESC, [5, "a"]);
    const tooShort = encodeCursor("file", DIRECTION.DESC, ["a"]);

    expect(decodeCursor(wrongType, "file", DIRECTION.DESC, shape)).toBeNull();
    expect(decodeCursor(tooShort, "file", DIRECTION.DESC, shape)).toBeNull();
  });
});

describe("paging rows by position", () => {
  const rows = Array.from({ length: 11 }, (_, index) => ({
    id: index,
    group: index % 3,
  }));
  const options = (after = null) => ({
    parts: parts(KIND.NUMBER, DIRECTION.ASC),
    keyOf: (row) => [row.group, row.id],
    sort: "group",
    direction: DIRECTION.ASC,
    after,
    limit: 4,
  });

  it("visits every row once however many share a value", () => {
    const walked = [];
    let after = null;
    do {
      const page = pageOf(rows, options(after));
      walked.push(...page.rows.map((row) => row.id));
      after = page.nextCursor
        ? decodeCursor(page.nextCursor, "group", DIRECTION.ASC, options().parts)
        : null;
    } while (after);

    expect(walked).toEqual([0, 3, 6, 9, 1, 4, 7, 10, 2, 5, 8]);
  });

  it("hands back no cursor when a page reaches the last row", () => {
    expect(pageOf(rows, { ...options(), limit: 11 }).nextCursor).toBeNull();
    expect(pageOf(rows, { ...options(), limit: 10 }).nextCursor).not.toBeNull();
  });
});
