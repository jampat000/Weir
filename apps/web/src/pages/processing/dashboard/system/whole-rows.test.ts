import { describe, expect, it } from "vitest";

import { columnsOf, rowsHeight, tilesBeyond, wholeRows } from "./whole-rows";

describe("whole rows", () => {
  it("counts the rows, with their gaps, that fit the room", () => {
    expect(wholeRows(89, 42, 5)).toBe(2);
    expect(wholeRows(88, 42, 5)).toBe(1);
    expect(wholeRows(136, 42, 5)).toBe(3);
  });

  it("is always at least one row, however little room there is", () => {
    expect(wholeRows(10, 42, 5)).toBe(1);
    expect(wholeRows(0, 42, 5)).toBe(1);
  });

  it("is as tall as its rows and the gaps between them", () => {
    expect(rowsHeight(1, 42, 5)).toBe(42);
    expect(rowsHeight(3, 42, 5)).toBe(136);
  });
});

describe("the tiles left beyond the rows", () => {
  it("counts the tiles after the last whole row", () => {
    expect(tilesBeyond(10, 2, 2)).toBe(6);
    expect(tilesBeyond(10, 3, 3)).toBe(1);
  });

  it("is none when every tile shows", () => {
    expect(tilesBeyond(4, 2, 3)).toBe(0);
  });
});

describe("a grid's columns", () => {
  it("counts the tracks of the computed template", () => {
    expect(columnsOf("91px 91px 91px")).toBe(3);
    expect(columnsOf("120.5px")).toBe(1);
  });

  it("is one column when the grid is not measured", () => {
    expect(columnsOf("")).toBe(1);
    expect(columnsOf("none")).toBe(1);
  });
});
