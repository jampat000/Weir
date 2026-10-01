import { act, render } from "@testing-library/react";
import { createRef } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { SLOTS_CHANGED_EVENT } from "./shelf-slots";
import { RESIZING_CLASS } from "./use-still-while-resizing";
import { TILE_KEY_ATTRIBUTE, useSlideNeighbours } from "./use-slide-neighbours";

const TILE_PX = 100;
let animate: ReturnType<typeof vi.fn>;
/** How far a slot opening beside the tiles has pushed them along, which is a change of layout and not of order. */
let pushed = 0;

function Row({
  keys,
  row,
  width = TILE_PX,
}: {
  keys: string[];
  row: React.RefObject<HTMLUListElement | null>;
  width?: number | null;
}) {
  useSlideNeighbours(row, keys.join("|"), width);
  return (
    <ul ref={row}>
      {keys.map((key) => (
        <li key={key} {...{ [TILE_KEY_ATTRIBUTE]: key }} />
      ))}
    </ul>
  );
}

/** Each tile sits at its place in the order, one tile width apart. */
function layOutInOrder() {
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(
    function (this: HTMLElement) {
      const row = this.parentElement;
      const index = row ? Array.from(row.children).indexOf(this) : -1;
      const left = this.tagName === "LI" ? index * TILE_PX + pushed : 0;
      return { left, top: 0, width: TILE_PX, height: 150 } as DOMRect;
    },
  );
}

beforeEach(() => {
  pushed = 0;
  animate = vi.fn();
  Object.defineProperty(HTMLElement.prototype, "animate", {
    configurable: true,
    writable: true,
    value: animate,
  });
  layOutInOrder();
});

afterEach(() => {
  vi.restoreAllMocks();
  delete (HTMLElement.prototype as { animate?: unknown }).animate;
  document.body.className = "";
});

describe("the tiles on the shelf when one is added", () => {
  it("do not slide when the shelf first appears", () => {
    render(<Row keys={["a", "b"]} row={createRef()} />);

    expect(animate).not.toHaveBeenCalled();
  });

  it("slide from where they were to where they are, and the new tile does not", () => {
    const row = createRef<HTMLUListElement>();
    const view = render(<Row keys={["a", "b"]} row={row} />);

    view.rerender(<Row keys={["n", "a", "b"]} row={row} />);

    expect(animate).toHaveBeenCalledTimes(2);
    expect(animate.mock.calls[0][0]).toEqual([
      { transform: `translateX(${-TILE_PX}px)` },
      { transform: "none" },
    ]);
    const slid = animate.mock.contexts.map((el: HTMLElement) =>
      el.getAttribute(TILE_KEY_ATTRIBUTE),
    );
    expect(slid).toEqual(["a", "b"]);
  });

  it("do not slide when only their width changed", () => {
    const row = createRef<HTMLUListElement>();
    const view = render(<Row keys={["a", "b"]} row={row} width={100} />);

    view.rerender(<Row keys={["a", "b"]} row={row} width={80} />);

    expect(animate).not.toHaveBeenCalled();
  });

  it("slide from where they stood when a slot last opened, not from before it", () => {
    const row = createRef<HTMLUListElement>();
    const view = render(<Row keys={["a", "b"]} row={row} />);

    // A slot beside them opened and pushed them 40px along, with no change of order.
    pushed = 40;
    act(() => {
      document.dispatchEvent(new Event(SLOTS_CHANGED_EVENT));
    });
    view.rerender(<Row keys={["n", "a", "b"]} row={row} />);

    expect(animate.mock.calls[0][0]).toEqual([
      { transform: `translateX(${-TILE_PX}px)` },
      { transform: "none" },
    ]);
  });

  it("do not slide while the window is being resized", () => {
    const row = createRef<HTMLUListElement>();
    const view = render(<Row keys={["a", "b"]} row={row} />);
    document.body.classList.add(RESIZING_CLASS);

    view.rerender(<Row keys={["n", "a", "b"]} row={row} />);

    expect(animate).not.toHaveBeenCalled();
  });
});
