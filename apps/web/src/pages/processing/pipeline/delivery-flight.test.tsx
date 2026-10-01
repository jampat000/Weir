import { act, render, renderHook, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import type { FinishedFile } from "../../../lib/activity/processing-outcome";
import type { LeavingCard } from "../leaving-cards";
import type { Lanes } from "../processing-model";
import { JustFinishedShelf } from "./just-finished-shelf";
import { PipelineBoard } from "./pipeline-board";
import { NOW, anEndedCard, lanesOf } from "./pipeline-fixtures";
import {
  RESIZING_CLASS,
  useStillWhileResizing,
} from "./use-still-while-resizing";

const delivered = anEndedCard(1, { kind: "done" });
const finished: FinishedFile = {
  id: 90,
  source: "download",
  kind: "cleaned",
  relativePath: delivered.path,
  libraryId: 2,
  savedBytes: 318 * 1024 ** 2,
  removedAudio: 2,
  removedSubtitles: 0,
  sentence: null,
  finishedAt: new Date(NOW - 5_000).toISOString(),
};

function setReducedMotion(reduce: boolean) {
  window.matchMedia = ((query: string) => ({
    matches: reduce && query.includes("prefers-reduced-motion"),
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    addListener: () => undefined,
    removeListener: () => undefined,
    dispatchEvent: () => false,
    onchange: null,
  })) as typeof window.matchMedia;
}

function page(lanes: Lanes, leaving: LeavingCard[]) {
  return (
    <MemoryRouter>
      <PipelineBoard
        lanes={lanes}
        leaving={leaving}
        filter="all"
        now={NOW}
        onOpen={vi.fn()}
      />
      <JustFinishedShelf
        items={[finished]}
        filter="all"
        now={NOW}
        onOpen={vi.fn()}
      />
    </MemoryRouter>
  );
}

const shelfTile = () =>
  document.querySelector<HTMLElement>("[data-shelf-path]") as HTMLElement;
const ghost = () => document.querySelector(".mm-tile--flying");

/** jsdom lays nothing out: give every box a size so there is somewhere to fly from and to. */
function giveEveryBoxASize() {
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockReturnValue({
    x: 10,
    y: 20,
    left: 10,
    top: 20,
    width: 50,
    height: 75,
    right: 60,
    bottom: 95,
    toJSON: () => ({}),
  });
}

type Flight = { onfinish: (() => void) | null; oncancel: (() => void) | null };
let flights: Flight[];

beforeEach(() => {
  setReducedMotion(false);
  flights = [];
  Object.defineProperty(HTMLElement.prototype, "animate", {
    configurable: true,
    writable: true,
    value: vi.fn(() => {
      const flight: Flight = { onfinish: null, oncancel: null };
      flights.push(flight);
      return flight;
    }),
  });
});

afterEach(() => {
  vi.restoreAllMocks();
  delete (HTMLElement.prototype as { animate?: unknown }).animate;
  document.body.className = "";
});

describe("a delivered file's tile", () => {
  it("is kept off the shelf while its card says Delivered", () => {
    render(page(lanesOf([]), [delivered]));

    expect(shelfTile().style.visibility).toBe("hidden");
  });

  it("flies from its card to the shelf when the hold ends, and shows there when it lands", () => {
    giveEveryBoxASize();
    const { rerender } = render(page(lanesOf([]), [delivered]));

    rerender(page(lanesOf([]), []));

    expect(ghost()).not.toBeNull();
    expect(shelfTile().style.visibility).toBe("hidden");

    act(() => flights[0].onfinish?.());

    expect(ghost()).toBeNull();
    expect(shelfTile().style.visibility).toBe("");
  });

  it("is simply on the shelf, with nothing flying, when movement is reduced", () => {
    setReducedMotion(true);
    giveEveryBoxASize();
    const { rerender } = render(page(lanesOf([]), [delivered]));

    expect(shelfTile().style.visibility).toBe("");
    rerender(page(lanesOf([]), []));

    expect(ghost()).toBeNull();
    expect(flights).toHaveLength(0);
    expect(shelfTile().style.visibility).toBe("");
  });

  it("is simply on the shelf when the page is not being looked at", () => {
    giveEveryBoxASize();
    const { rerender } = render(page(lanesOf([]), [delivered]));
    vi.spyOn(document, "visibilityState", "get").mockReturnValue("hidden");

    rerender(page(lanesOf([]), []));

    expect(ghost()).toBeNull();
    expect(shelfTile().style.visibility).toBe("");
  });

  it("is simply on the shelf when the file has no tile there", () => {
    giveEveryBoxASize();
    const { rerender } = render(
      <MemoryRouter>
        <PipelineBoard
          lanes={lanesOf([])}
          leaving={[delivered]}
          filter="all"
          now={NOW}
          onOpen={vi.fn()}
        />
      </MemoryRouter>,
    );

    rerender(
      <MemoryRouter>
        <PipelineBoard
          lanes={lanesOf([])}
          leaving={[]}
          filter="all"
          now={NOW}
          onOpen={vi.fn()}
        />
      </MemoryRouter>,
    );

    expect(ghost()).toBeNull();
  });

  it("is shown on the shelf if the board goes away before the hold ends", () => {
    const ofBoard = () => (
      <MemoryRouter>
        <PipelineBoard
          lanes={lanesOf([])}
          leaving={[delivered]}
          filter="all"
          now={NOW}
          onOpen={vi.fn()}
        />
      </MemoryRouter>
    );
    const board = render(ofBoard());
    render(
      <MemoryRouter>
        <JustFinishedShelf
          items={[finished]}
          filter="all"
          now={NOW}
          onOpen={vi.fn()}
        />
      </MemoryRouter>,
    );
    // The page ticks every second, so the board looks again and finds the shelf's tile.
    board.rerender(ofBoard());
    expect(shelfTile().style.visibility).toBe("hidden");

    board.unmount();

    expect(shelfTile().style.visibility).toBe("");
  });
});

describe("nothing animates while the window is being resized", () => {
  afterEach(() => vi.useRealTimers());

  it("marks the page while the size is changing and clears it 350ms after the last change", () => {
    vi.useFakeTimers();
    renderHook(() => useStillWhileResizing());
    expect(document.body.classList.contains(RESIZING_CLASS)).toBe(false);

    act(() => {
      Object.defineProperty(window, "innerWidth", {
        configurable: true,
        value: window.innerWidth + 40,
      });
      window.dispatchEvent(new Event("resize"));
    });
    expect(document.body.classList.contains(RESIZING_CLASS)).toBe(true);

    act(() => {
      vi.advanceTimersByTime(340);
    });
    expect(document.body.classList.contains(RESIZING_CLASS)).toBe(true);
    act(() => {
      vi.advanceTimersByTime(20);
    });
    expect(document.body.classList.contains(RESIZING_CLASS)).toBe(false);
  });

  it("is on while the board is on screen, and clears the mark when it goes", () => {
    const { unmount } = render(page(lanesOf([]), []));
    act(() => {
      Object.defineProperty(window, "innerHeight", {
        configurable: true,
        value: window.innerHeight + 40,
      });
      window.dispatchEvent(new Event("resize"));
    });
    expect(document.body.classList.contains(RESIZING_CLASS)).toBe(true);

    unmount();

    expect(document.body.classList.contains(RESIZING_CLASS)).toBe(false);
    expect(screen.queryByTestId("pipeline-board")).toBeNull();
  });
});
