import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import type { LibraryOverview } from "../../lib/processing/library-mode-api";
import {
  LIBRARY_CHIP_COUNT,
  LibraryFilters,
  NO_FILTER,
  type LibraryFilter,
} from "./library-filters";

const OVERVIEW = {
  totals: {
    files: 16,
    by_status: {
      needs_cleaning: 0,
      cleaning: 0,
      matches: 14,
      cant_clean_yet: 1,
      left_alone: 1,
    },
  },
} as unknown as LibraryOverview;

function renderFilters(filter: LibraryFilter, folded: number) {
  const onFilter = vi.fn();
  render(
    <LibraryFilters
      search=""
      onSearch={() => undefined}
      overview={OVERVIEW}
      filter={filter}
      onFilter={onFilter}
      fit={0}
      folded={folded}
      onRow={() => undefined}
    />,
  );
  return onFilter;
}

const shown = () =>
  within(screen.getByRole("group", { name: "Show" }))
    .getAllByRole("button")
    .map((button) => button.textContent);

describe("LibraryFilters", () => {
  it("shows every status, with its count, when nothing is folded", () => {
    renderFilters(NO_FILTER, 0);

    expect(LIBRARY_CHIP_COUNT).toBe(6);
    expect(shown()).toEqual([
      "All 16",
      "Needs cleaning 0",
      "Cleaning 0",
      "Matches rules 14",
      "Can't clean 1",
      "Left alone 1",
    ]);
  });

  it("folds the last statuses into More, whole, with their counts, and still filters by them", () => {
    const onFilter = renderFilters(NO_FILTER, 3);

    expect(shown()).toEqual([
      "All 16",
      "Needs cleaning 0",
      "Cleaning 0",
      "More",
    ]);
    fireEvent.click(screen.getByRole("button", { name: "More" }));
    const menu = screen.getByRole("menu", { name: "More statuses" });
    expect(
      within(menu)
        .getAllByRole("menuitem")
        .map((item) => item.textContent),
    ).toEqual(["Matches rules 14", "Can't clean 1", "Left alone 1"]);

    fireEvent.click(within(menu).getByRole("menuitem", { name: /Left alone/ }));
    expect(onFilter).toHaveBeenCalledWith({
      status: "left_alone",
      problem: null,
    });
  });

  it("never folds the status that is chosen", () => {
    renderFilters({ status: "left_alone", problem: null }, 3);

    expect(shown()).toEqual([
      "All 16",
      "Needs cleaning 0",
      "Left alone 1",
      "More",
    ]);
  });
});
