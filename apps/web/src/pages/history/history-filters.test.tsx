import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import {
  ShellHeaderProvider,
  useHeaderSlotRef,
} from "../../components/shell/shell-header-context";
import type { ProcessingLibrary } from "../../lib/processing/libraries-api";
import { HISTORY_GROUPS, type HistoryGroup } from "./history-entries";
import { HistoryFilters } from "./history-filters";

const COUNTS = Object.fromEntries(
  HISTORY_GROUPS.map((g, index) => [g.id, index + 1]),
) as Record<HistoryGroup, number>;

const LIBRARIES = [
  { id: 4, name: "Movies" },
  { id: 9, name: "TV" },
] as ProcessingLibrary[];

/** The shell's header, reduced to the slot a page's own controls go in. */
function HeaderSlot() {
  const slotRef = useHeaderSlotRef();
  return <div data-testid="header-slot" ref={slotRef} />;
}

function renderFilters(setParam = vi.fn()) {
  render(
    <ShellHeaderProvider>
      <HeaderSlot />
      <HistoryFilters
        query=""
        group="all"
        counts={COUNTS}
        libraryId={undefined}
        periodId="7"
        libraries={LIBRARIES}
        setParam={setParam}
      />
    </ShellHeaderProvider>,
  );
  return setParam;
}

describe("HistoryFilters in the header", () => {
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
    ).toEqual(HISTORY_GROUPS.map((g) => `${g.label} ${COUNTS[g.id]}`));
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
});
