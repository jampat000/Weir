import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { PageTabs } from "./page-tabs";

const TABS = ["One", "Two", "Three", "Four", "Five", "Six"].map((label) => ({
  id: label.toLowerCase(),
  label,
}));
const TAB_WIDTH = 80;
const MORE_WIDTH = 60;

/** Lays every tab out 80px wide and More 60px wide, in a box `room.current` px wide. */
function layOut(room: { current: number }) {
  return vi
    .spyOn(HTMLElement.prototype, "getBoundingClientRect")
    .mockImplementation(function (this: HTMLElement) {
      const label = this.dataset.label;
      let width = 0;
      if (this.classList.contains("mm-page-tabs-fit")) width = room.current;
      else if (label === "More") width = MORE_WIDTH;
      else if (label) width = TAB_WIDTH;
      return {
        width,
        height: 14,
        top: 0,
        left: 0,
        right: width,
        bottom: 14,
        x: 0,
        y: 0,
        toJSON: () => ({}),
      };
    });
}

function Tabs({
  initial = "one",
  placement = "title",
  onSelect,
}: {
  initial?: string;
  placement?: "title" | "row";
  onSelect?: (id: string) => void;
}) {
  const [activeId, setActiveId] = useState(initial);
  return (
    <>
      <PageTabs
        tabs={TABS}
        activeId={activeId}
        onSelect={(id) => {
          setActiveId(id);
          onSelect?.(id);
        }}
        ariaLabel="Example sections"
        idPrefix="example-tab"
        placement={placement}
      />
      <button type="button">Elsewhere</button>
    </>
  );
}

function shownTabs() {
  const list = screen.getByRole("tablist", { name: "Example sections" });
  return within(list)
    .getAllByRole("tab")
    .map((tab) => tab.textContent);
}

const moreButton = () => screen.getByRole("button", { name: "More" });

/** A key pressed on whatever has focus. */
function press(key: string) {
  fireEvent.keyDown(document.activeElement ?? document.body, { key });
}

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("page tabs that do not fit", () => {
  it("shows every tab and no More when they all fit", () => {
    layOut({ current: 570 });
    render(<Tabs />);

    expect(shownTabs()).toEqual(["One", "Two", "Three", "Four", "Five", "Six"]);
    expect(screen.queryByRole("button", { name: "More" })).toBeNull();
  });

  it("folds the tabs that do not fit into a More menu", () => {
    layOut({ current: 300 });
    render(<Tabs />);

    expect(shownTabs()).toEqual(["One", "Two"]);
    expect(moreButton()).toHaveAttribute("aria-haspopup", "menu");
    expect(moreButton()).toHaveAttribute("aria-expanded", "false");

    fireEvent.click(moreButton());

    expect(moreButton()).toHaveAttribute("aria-expanded", "true");
    const menu = screen.getByRole("menu", { name: "More example sections" });
    expect(
      within(menu)
        .getAllByRole("menuitem")
        .map((item) => item.textContent),
    ).toEqual(["Three", "Four", "Five", "Six"]);
  });

  it("never draws More as a tab, and never folds the chosen tab", () => {
    layOut({ current: 300 });
    render(<Tabs initial="six" />);

    expect(shownTabs()).toEqual(["One", "Six"]);
    expect(screen.getByRole("tab", { name: "Six" })).toHaveAttribute(
      "aria-selected",
      "true",
    );
    expect(moreButton()).not.toHaveAttribute("aria-selected");
  });

  it("does not fold the tabs of a row under the title", () => {
    layOut({ current: 300 });
    render(<Tabs placement="row" />);

    expect(shownTabs()).toHaveLength(6);
    expect(screen.queryByRole("button", { name: "More" })).toBeNull();
  });

  it("folds again, and unfolds, as the room changes", () => {
    const room = { current: 570 };
    layOut(room);
    let resized = () => {};
    vi.stubGlobal(
      "ResizeObserver",
      class {
        constructor(callback: () => void) {
          resized = callback;
        }
        observe() {}
        disconnect() {}
      },
    );
    render(<Tabs />);
    expect(shownTabs()).toHaveLength(6);

    room.current = 300;
    act(() => resized());
    expect(shownTabs()).toEqual(["One", "Two"]);

    room.current = 570;
    act(() => resized());
    expect(shownTabs()).toHaveLength(6);
  });
});

describe("the More menu", () => {
  it("opens with ArrowDown on the button and puts focus on the first item", () => {
    layOut({ current: 300 });
    render(<Tabs />);
    moreButton().focus();

    press("ArrowDown");

    expect(screen.getByRole("menuitem", { name: "Three" })).toHaveFocus();
  });

  it("moves between items with the arrow keys, Home and End, wrapping at the ends", () => {
    layOut({ current: 300 });
    render(<Tabs />);
    fireEvent.click(moreButton());

    press("ArrowUp");
    expect(screen.getByRole("menuitem", { name: "Six" })).toHaveFocus();

    press("ArrowDown");
    expect(screen.getByRole("menuitem", { name: "Three" })).toHaveFocus();

    press("End");
    expect(screen.getByRole("menuitem", { name: "Six" })).toHaveFocus();

    press("Home");
    expect(screen.getByRole("menuitem", { name: "Three" })).toHaveFocus();
  });

  it("closes on Escape and gives focus back to More", () => {
    layOut({ current: 300 });
    render(<Tabs />);
    fireEvent.click(moreButton());

    press("Escape");

    expect(screen.queryByRole("menu")).toBeNull();
    expect(moreButton()).toHaveFocus();
  });

  it("closes on a click outside it", () => {
    layOut({ current: 300 });
    render(<Tabs />);
    fireEvent.click(moreButton());

    fireEvent.mouseDown(screen.getByRole("button", { name: "Elsewhere" }));

    expect(screen.queryByRole("menu")).toBeNull();
  });

  it("stays open on a click inside it that is not on an item", () => {
    layOut({ current: 300 });
    render(<Tabs />);
    fireEvent.click(moreButton());

    fireEvent.mouseDown(screen.getByRole("menu"));

    expect(screen.getByRole("menu")).toBeInTheDocument();
  });

  it("chooses the tab it names, which takes the last place in the row and the focus", () => {
    layOut({ current: 300 });
    const onSelect = vi.fn();
    render(<Tabs onSelect={onSelect} />);
    fireEvent.click(moreButton());

    fireEvent.click(screen.getByRole("menuitem", { name: "Four" }));

    expect(onSelect).toHaveBeenCalledWith("four");
    expect(screen.queryByRole("menu")).toBeNull();
    expect(shownTabs()).toEqual(["One", "Four"]);
    expect(screen.getByRole("tab", { name: "Four" })).toHaveFocus();
  });

  it("is reached from the tab keys: an arrow key onto a folded tab chooses it and focuses it", () => {
    layOut({ current: 300 });
    render(<Tabs initial="two" />);
    screen.getByRole("tab", { name: "Two" }).focus();

    press("ArrowRight");

    expect(shownTabs()).toEqual(["One", "Three"]);
    expect(screen.getByRole("tab", { name: "Three" })).toHaveFocus();
  });
});
