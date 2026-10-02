import { act, render, renderHook, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { WithWorkflows } from "../../../test/with-workflows";
import type { FinishedFile } from "../../../lib/activity/processing-outcome";
import type { LeavingCard } from "../leaving-cards";
import type { Lanes } from "../processing-model";
import { FLIGHT_MS, LANDING_FADE_MS, easeInOut } from "./flight-path";
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
    <WithWorkflows>
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
    </WithWorkflows>
  );
}

const shelfTile = () =>
  document.querySelector<HTMLElement>("[data-shelf-path]") as HTMLElement;
const ghost = () => document.querySelector<HTMLElement>(".mm-tile--flying");

/** The poster on the card is 50x75 at the top left; the shelf's is twice the size, lower down and to the right. */
const CARD_POSTER = { left: 10, top: 20, width: 50, height: 75 };
let shelfPoster = { left: 300, top: 400, width: 100, height: 150 };

function rectOf({ left, top, width, height }: typeof CARD_POSTER): DOMRect {
  return {
    x: left,
    y: top,
    left,
    top,
    width,
    height,
    right: left + width,
    bottom: top + height,
    toJSON: () => ({}),
  };
}

/** jsdom lays nothing out: the two posters are given places, and every other box a size of its own. */
function placePosters() {
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(
    function (this: HTMLElement) {
      if (this.hasAttribute("data-shelf-path")) return rectOf(shelfPoster);
      if (this.hasAttribute("data-pipeline-tile")) return rectOf(CARD_POSTER);
      return rectOf({ left: 0, top: 0, width: 10, height: 10 });
    },
  );
}

/** The browser's frames, run by hand at the times a test chooses. */
let frames: FrameRequestCallback[];
let clock: number;
function runFramesAt(ms: number) {
  clock = ms;
  const due = frames.splice(0);
  act(() => due.forEach((frame) => frame(ms)));
}

beforeEach(() => {
  setReducedMotion(false);
  frames = [];
  clock = 0;
  shelfPoster = { left: 300, top: 400, width: 100, height: 150 };
  vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout"] });
  vi.spyOn(performance, "now").mockImplementation(() => clock);
  vi.spyOn(window, "requestAnimationFrame").mockImplementation((frame) => {
    frames.push(frame);
    return frames.length;
  });
  vi.spyOn(window, "cancelAnimationFrame").mockImplementation(() => undefined);
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
  document.body.className = "";
  document.querySelectorAll(".mm-tile--flying").forEach((el) => el.remove());
});

/** Renders the board with the file's card holding "Delivered", then lets the hold end so the poster takes off. */
function takeOff() {
  placePosters();
  const view = render(page(lanesOf([]), [delivered]));
  view.rerender(page(lanesOf([]), []));
  return view;
}

describe("a delivered file's poster", () => {
  it("is kept off the shelf while its card says Delivered", () => {
    render(page(lanesOf([]), [delivered]));

    expect(shelfTile().style.visibility).toBe("hidden");
  });

  it("flies from its card at the card's own size, and the shelf's tile stays out of sight until it lands", () => {
    takeOff();

    const flying = ghost();
    expect(flying).not.toBeNull();
    expect(flying?.style.width).toBe("50px");
    expect(flying?.style.height).toBe("75px");
    runFramesAt(0);
    runFramesAt(FLIGHT_MS / 2);
    expect(flying?.style.transform).toMatch(/^translate\(.+\) scale\(/);
    expect(shelfTile().style.visibility).toBe("hidden");
  });

  it("flies in a frame the size of the card's tile, so the poster's own proportions are those it had on the card", () => {
    takeOff();

    const frame = ghost();
    expect(frame?.parentElement).toBe(document.body);
    expect(frame?.firstElementChild?.classList.contains("mm-tile")).toBe(true);
    expect(frame?.firstElementChild?.parentElement).toBe(frame);
  });

  it("keeps its shape all the way, scaling by one factor", () => {
    takeOff();

    runFramesAt(0);
    runFramesAt(FLIGHT_MS * 0.7);

    expect(ghost()?.style.transform.match(/scale\(/g)).toHaveLength(1);
    expect(ghost()?.style.transform).not.toMatch(/scale\([^)]*,/);
  });

  it("lands exactly on the shelf's tile and shows it underneath before it fades away", () => {
    takeOff();

    runFramesAt(0);
    runFramesAt(FLIGHT_MS);

    expect(ghost()?.style.transform).toContain("translate(300px, 400px)");
    expect(ghost()?.style.transform).toContain("scale(2)");
    expect(shelfTile().style.visibility).toBe("");
    expect(ghost()).not.toBeNull();

    runFramesAt(FLIGHT_MS + LANDING_FADE_MS / 2);
    expect(Number(ghost()?.style.opacity)).toBeCloseTo(0.5);

    runFramesAt(FLIGHT_MS + LANDING_FADE_MS);
    expect(ghost()).toBeNull();
    expect(shelfTile().style.visibility).toBe("");
  });

  it("follows the shelf tile when the shelf moves during the flight", () => {
    takeOff();
    runFramesAt(0);
    runFramesAt(FLIGHT_MS / 2);

    shelfPoster = { left: 372, top: 380, width: 100, height: 150 };
    runFramesAt(FLIGHT_MS);

    expect(ghost()?.style.transform).toContain("translate(372px, 380px)");
  });

  it("is dropped when the shelf tile goes away mid-flight", () => {
    const view = takeOff();
    runFramesAt(0);

    view.unmount();
    runFramesAt(FLIGHT_MS / 2);

    expect(ghost()).toBeNull();
  });

  it("is ended all the same when the page stops drawing frames", () => {
    takeOff();

    act(() => {
      vi.advanceTimersByTime(FLIGHT_MS + LANDING_FADE_MS + 600);
    });

    expect(ghost()).toBeNull();
    expect(shelfTile().style.visibility).toBe("");
  });

  it("is simply on the shelf, with nothing flying, when movement is reduced", () => {
    setReducedMotion(true);
    placePosters();
    const { rerender } = render(page(lanesOf([]), [delivered]));

    expect(shelfTile().style.visibility).toBe("");
    rerender(page(lanesOf([]), []));

    expect(ghost()).toBeNull();
    expect(frames).toHaveLength(0);
    expect(shelfTile().style.visibility).toBe("");
  });

  it("is simply on the shelf when the page is not being looked at", () => {
    placePosters();
    const { rerender } = render(page(lanesOf([]), [delivered]));
    vi.spyOn(document, "visibilityState", "get").mockReturnValue("hidden");

    rerender(page(lanesOf([]), []));

    expect(ghost()).toBeNull();
    expect(shelfTile().style.visibility).toBe("");
  });

  it("is simply on the shelf while the window is being resized", () => {
    placePosters();
    const { rerender } = render(page(lanesOf([]), [delivered]));
    document.body.classList.add(RESIZING_CLASS);

    rerender(page(lanesOf([]), []));

    expect(ghost()).toBeNull();
    expect(shelfTile().style.visibility).toBe("");
  });

  it("is simply on the shelf when the file has no tile there", () => {
    placePosters();
    const { rerender } = render(
      <WithWorkflows>
        <MemoryRouter>
          <PipelineBoard
            lanes={lanesOf([])}
            leaving={[delivered]}
            filter="all"
            now={NOW}
            onOpen={vi.fn()}
          />
        </MemoryRouter>
      </WithWorkflows>,
    );

    rerender(
      <WithWorkflows>
        <MemoryRouter>
          <PipelineBoard
            lanes={lanesOf([])}
            leaving={[]}
            filter="all"
            now={NOW}
            onOpen={vi.fn()}
          />
        </MemoryRouter>
      </WithWorkflows>,
    );

    expect(ghost()).toBeNull();
  });

  it("is shown on the shelf if the board goes away before the hold ends", () => {
    const ofBoard = () => (
      <WithWorkflows>
        <MemoryRouter>
          <PipelineBoard
            lanes={lanesOf([])}
            leaving={[delivered]}
            filter="all"
            now={NOW}
            onOpen={vi.fn()}
          />
        </MemoryRouter>
      </WithWorkflows>
    );
    const board = render(ofBoard());
    render(
      <WithWorkflows>
        <MemoryRouter>
          <JustFinishedShelf
            items={[finished]}
            filter="all"
            now={NOW}
            onOpen={vi.fn()}
          />
        </MemoryRouter>
      </WithWorkflows>,
    );
    // The page ticks every second, so the board looks again and finds the shelf's tile.
    board.rerender(ofBoard());
    expect(shelfTile().style.visibility).toBe("hidden");

    board.unmount();

    expect(shelfTile().style.visibility).toBe("");
  });
});

const slotOfTile = () => shelfTile().closest("li") as HTMLElement;
const tilesOnShelf = () =>
  document.querySelectorAll("[data-shelf-path]").length;

describe("the shelf's slot for a delivered file", () => {
  it("is closed while its card says Delivered: no width, no gap, so nothing on the shelf moves", () => {
    render(page(lanesOf([]), [delivered]));

    const slot = slotOfTile();
    expect(slot.style.width).toBe("0px");
    expect(slot.style.marginRight).toBe("-12px");
    expect(slot.style.overflow).toBe("hidden");
  });

  it("opens over the flight by the flight's own curve, the neighbours pushed along by the width it has taken", () => {
    takeOff();

    runFramesAt(0);
    expect(slotOfTile().style.width).toBe("0px");
    runFramesAt(FLIGHT_MS / 4);
    const share = easeInOut(0.25);
    // The art is 100px wide; the gap taken back shrinks by the same share.
    expect(parseFloat(slotOfTile().style.width)).toBeCloseTo(100 * share);
    expect(parseFloat(slotOfTile().style.marginRight)).toBeCloseTo(
      -12 * (1 - share),
    );
    runFramesAt(FLIGHT_MS / 2);
    expect(slotOfTile().style.width).toBe("50px");
    expect(slotOfTile().style.marginRight).toBe("-6px");
  });

  it("is open as far as it goes, and settled, as the poster lands", () => {
    takeOff();

    runFramesAt(0);
    runFramesAt(FLIGHT_MS);

    const slot = slotOfTile();
    expect(slot.style.width).toBe("");
    expect(slot.style.marginRight).toBe("");
    expect(slot.style.overflow).toBe("");
    expect(shelfTile().style.visibility).toBe("");
  });

  it("is never closed when movement is reduced", () => {
    setReducedMotion(true);
    placePosters();
    const { rerender } = render(page(lanesOf([]), [delivered]));
    expect(slotOfTile().style.width).toBe("");

    rerender(page(lanesOf([]), []));

    expect(slotOfTile().style.width).toBe("");
  });

  it("is open at once when the window is being resized as the poster would take off", () => {
    placePosters();
    const { rerender } = render(page(lanesOf([]), [delivered]));
    expect(slotOfTile().style.width).toBe("0px");
    document.body.classList.add(RESIZING_CLASS);

    rerender(page(lanesOf([]), []));

    expect(slotOfTile().style.width).toBe("");
  });

  it("keeps the last tile on the shelf until the slot has opened and the poster has landed", () => {
    vi.spyOn(Element.prototype, "clientHeight", "get").mockReturnValue(100);
    vi.spyOn(Element.prototype, "clientWidth", "get").mockReturnValue(60);
    const other: FinishedFile = {
      ...finished,
      id: 91,
      relativePath: "Other.mkv",
    };
    const both = (leaving: LeavingCard[]) => (
      <WithWorkflows>
        <MemoryRouter>
          <PipelineBoard
            lanes={lanesOf([])}
            leaving={leaving}
            filter="all"
            now={NOW}
            onOpen={vi.fn()}
          />
          <JustFinishedShelf
            items={[finished, other]}
            filter="all"
            now={NOW}
            onOpen={vi.fn()}
          />
        </MemoryRouter>
      </WithWorkflows>
    );
    placePosters();

    // One tile fits the shelf: the delivered file's slot is closed, and the file beside it is where it was.
    const view = render(both([delivered]));
    expect(tilesOnShelf()).toBe(2);

    view.rerender(both([]));
    runFramesAt(0);
    runFramesAt(FLIGHT_MS);
    expect(tilesOnShelf()).toBe(2);

    // The poster has landed and faded: the pushed-out tile is gone.
    runFramesAt(FLIGHT_MS + LANDING_FADE_MS);
    expect(tilesOnShelf()).toBe(1);
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
