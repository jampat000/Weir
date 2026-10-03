import { describe, expect, it } from "vitest";

import { logGrid, logSortQuery } from "./log-columns";

describe("the log's sort", () => {
  it("asks for nothing when the sort is the one the server gives unasked", () => {
    expect(logSortQuery({ id: "time", direction: "desc" })).toEqual({});
    expect(logSortQuery(null)).toEqual({});
  });

  it("asks the server for the column and the way it runs", () => {
    expect(logSortQuery({ id: "level", direction: "asc" })).toEqual({
      sort: "level",
      direction: "asc",
    });
    expect(logSortQuery({ id: "time", direction: "asc" })).toEqual({
      sort: "time",
      direction: "asc",
    });
  });

  it("never asks to sort by what a row said, which the server cannot", () => {
    expect(logSortQuery({ id: "title", direction: "asc" })).toEqual({});
  });
});

describe("the log's grid", () => {
  it("lays the columns out in their order and leaves the arrow last", () => {
    const grid = logGrid([
      "level",
      "time",
      "source",
      "category",
      "workflow",
      "title",
    ]);

    expect(grid["--log-cols"]).toBe(
      "3rem 5.5rem 4rem 6.5rem 8rem minmax(0, 1fr) 0.875rem",
    );
  });

  it("gives the time column room for a day in front of the time where no heading names the day", () => {
    const order = ["time", "level", "title"] as const;

    expect(logGrid(order)["--log-cols"]).toBe(
      "5.5rem 3rem minmax(0, 1fr) 0.875rem",
    );
    expect(logGrid(order, true)["--log-cols"]).toBe(
      "10.25rem 3rem minmax(0, 1fr) 0.875rem",
    );
    expect(
      logGrid(["time", "level", "source", "category"], true)[
        "--log-cols-narrow"
      ],
    ).toBe("10.25rem 3rem auto minmax(0, 1fr) 0.875rem");
  });

  it("leaves out the workflow and the title once the card is narrow", () => {
    const grid = logGrid([
      "title",
      "category",
      "time",
      "workflow",
      "level",
      "source",
    ]);

    expect(grid["--log-cols-narrow"]).toBe(
      "minmax(0, 1fr) 5.5rem 3rem auto 0.875rem",
    );
  });
});
