import { fireEvent, render, screen, within } from "@testing-library/react";
import { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import {
  ShellHeaderProvider,
  useHeaderSlotRef,
} from "../../components/shell/shell-header-context";
import type { ProcessingLibrary } from "../../lib/processing/libraries-api";
import { ACTIVITY_GROUPS, type ActivityGroup } from "./activity-entries";
import { ActivityFilters } from "./activity-filters";

const COUNTS = Object.fromEntries(
  ACTIVITY_GROUPS.map((g, index) => [g.id, index + 1]),
) as Record<ActivityGroup, number>;

const LIBRARIES = [
  { id: 4, name: "Movies" },
  { id: 9, name: "TV" },
] as ProcessingLibrary[];

/** The shell's header, reduced to the slot a page's own controls go in. */
function HeaderSlot() {
  const slotRef = useHeaderSlotRef();
  return <div data-testid="header-slot" ref={slotRef} />;
}

/** The filters, with the header slot and, when asked, the slot a Files card holds for the pickers. */
function Harness({
  setParam,
  group,
  withCard,
}: {
  setParam: () => void;
  group: ActivityGroup;
  withCard: boolean;
}) {
  const [card, setCard] = useState<HTMLDivElement | null>(null);
  return (
    <ShellHeaderProvider>
      <HeaderSlot />
      <div ref={setCard} data-testid="card" />
      <ActivityFilters
        query=""
        group={group}
        counts={COUNTS}
        libraryId={undefined}
        periodId="7"
        libraries={LIBRARIES}
        setParam={setParam}
        cardSlot={withCard ? card : null}
      />
    </ShellHeaderProvider>
  );
}

function renderFilters(
  setParam = vi.fn(),
  { group = "all" as ActivityGroup, withCard = false } = {},
) {
  render(<Harness setParam={setParam} group={group} withCard={withCard} />);
  return setParam;
}

/** Makes the chips' box too wide for its room for as long as `unfit` says so; jsdom has no layout of its own. */
function chipsOverflowWhile(unfit: (row: HTMLElement) => boolean) {
  vi.spyOn(Element.prototype, "scrollWidth", "get").mockImplementation(
    function (this: Element) {
      return this.classList.contains("mm-history-chips") &&
        unfit(this as HTMLElement)
        ? 500
        : 0;
    },
  );
}

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("ActivityFilters in the header", () => {
  it("puts the search, the kinds of file and both pickers on the title line, in the header's slot", () => {
    renderFilters();

    const slot = screen.getByTestId("header-slot");
    expect(
      within(slot).getByRole("searchbox", { name: "Find a file" }),
    ).toBeInTheDocument();
    const chips = within(slot).getByRole("group", { name: "Show" });
    expect(
      within(chips)
        .getAllByRole("button")
        .map((chip) => chip.textContent),
    ).toEqual(ACTIVITY_GROUPS.map((g) => `${g.label} ${COUNTS[g.id]}`));
    expect(within(slot).getByLabelText("Workflow")).toBeInTheDocument();
    expect(within(slot).getByLabelText("How far back")).toBeInTheDocument();
  });

  it("keeps the address parameters each control writes", () => {
    const setParam = renderFilters();

    fireEvent.click(screen.getByRole("button", { name: /Failed/ }));
    expect(setParam).toHaveBeenLastCalledWith("show", "failed");
    fireEvent.click(screen.getByRole("button", { name: /All/ }));
    expect(setParam).toHaveBeenLastCalledWith("show", null);
    fireEvent.click(screen.getByRole("button", { name: "Workflow" }));
    fireEvent.click(screen.getByRole("option", { name: "TV" }));
    expect(setParam).toHaveBeenLastCalledWith("library", "9");
    fireEvent.click(screen.getByRole("button", { name: "How far back" }));
    fireEvent.click(screen.getByRole("option", { name: "Last 30 days" }));
    expect(setParam).toHaveBeenLastCalledWith("within", "30");
  });

  it("searches on Enter, and Escape leaves the box", () => {
    const setParam = renderFilters();
    const search = screen.getByRole("searchbox", { name: "Find a file" });

    fireEvent.change(search, { target: { value: " metropolis " } });
    fireEvent.submit(search);
    expect(setParam).toHaveBeenLastCalledWith("q", "metropolis");

    search.focus();
    fireEvent.keyDown(search, { key: "Escape" });
    expect(search).not.toHaveFocus();
  });

  describe("when the title line is short of room", () => {
    const sharedLine = () =>
      vi.stubGlobal("matchMedia", () => ({
        matches: true,
        addEventListener: () => undefined,
        removeEventListener: () => undefined,
      }));
    const kindButtons = () =>
      within(
        within(screen.getByTestId("header-slot")).getByRole("group", {
          name: "Show",
        }),
      )
        .getAllByRole("button")
        .map((button) => button.textContent);

    it("shrinks the search to a mark, then moves the pickers into the Files card", () => {
      sharedLine();
      chipsOverflowWhile(
        (row) =>
          row
            .closest(".mm-history-controls")
            ?.querySelector(".mm-history-scope") != null,
      );
      renderFilters(vi.fn(), { withCard: true });

      const card = screen.getByTestId("card");
      expect(within(card).getByLabelText("Workflow")).toBeInTheDocument();
      expect(within(card).getByLabelText("How far back")).toBeInTheDocument();
      expect(
        within(screen.getByTestId("header-slot")).queryByLabelText("Workflow"),
      ).not.toBeInTheDocument();
      expect(
        screen.getByRole("searchbox", { name: "Find a file" }).closest("label"),
      ).toHaveAttribute("data-collapsed", "true");
      expect(kindButtons()).toHaveLength(ACTIVITY_GROUPS.length);
    });

    it("keeps the pickers on the title line where the page has no card for them", () => {
      sharedLine();
      chipsOverflowWhile(() => false);
      renderFilters(vi.fn(), { withCard: false });

      expect(
        within(screen.getByTestId("header-slot")).getByLabelText("Workflow"),
      ).toBeInTheDocument();
    });

    it("folds the last kinds of file into More when that is still not enough, whole and in order", () => {
      sharedLine();
      chipsOverflowWhile(
        (row) => row.querySelectorAll("[aria-pressed]").length > 4,
      );
      renderFilters(vi.fn(), { withCard: true });

      expect(kindButtons()).toEqual([
        "All 1",
        "In progress 2",
        "Finished 3",
        "Needs you 4",
        "More",
      ]);
      fireEvent.click(screen.getByRole("button", { name: "More" }));
      const menu = screen.getByRole("menu", { name: "More kinds of file" });
      expect(
        within(menu)
          .getAllByRole("menuitem")
          .map((item) => item.textContent),
      ).toEqual(["On hold 5", "Skipped 6", "Failed 7", "Kept 8"]);
    });

    it("never folds the chosen kind of file", () => {
      sharedLine();
      chipsOverflowWhile(
        (row) => row.querySelectorAll("[aria-pressed]").length > 2,
      );
      renderFilters(vi.fn(), { group: "kept", withCard: true });

      expect(kindButtons()).toEqual(["All 1", "Kept 8", "More"]);
    });

    it("chooses a folded kind of file from the menu", () => {
      sharedLine();
      chipsOverflowWhile(
        (row) => row.querySelectorAll("[aria-pressed]").length > 4,
      );
      const setParam = renderFilters(vi.fn(), { withCard: true });

      fireEvent.click(screen.getByRole("button", { name: "More" }));
      fireEvent.click(screen.getByRole("menuitem", { name: /Failed/ }));

      expect(setParam).toHaveBeenLastCalledWith("show", "failed");
    });
  });
});
