import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { SETTLE_MS } from "../../lib/ui/drag-motion";
import { ReorderHandle } from "./reorder-handle";
import { useRowReorder } from "./use-row-reorder";

const ROW_HEIGHT = 100;
const NAMES: Record<number, string> = { 1: "Movies", 2: "TV", 3: "Kids" };

/** Lays the rows out one under another in the order the page draws them, shifted by the offset they have been given. */
function layOutInDrawnOrder() {
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(
    function (this: HTMLElement) {
      const index = this.parentElement
        ? Array.from(this.parentElement.children).indexOf(this)
        : 0;
      const offset = /translateY\((-?[\d.]+)px\)/.exec(this.style.transform);
      const top = 100 + index * ROW_HEIGHT + Number(offset?.[1] ?? 0);
      return {
        top,
        bottom: top + ROW_HEIGHT,
        left: 0,
        right: 300,
        width: 300,
        height: ROW_HEIGHT,
        x: 0,
        y: top,
        toJSON: () => ({}),
      };
    },
  );
}

function Rows({ onCommit }: { onCommit: (ids: number[]) => Promise<unknown> }) {
  const reorder = useRowReorder({
    ids: [1, 2, 3],
    nameOf: (id) => NAMES[id],
    onCommit,
  });
  return (
    <table>
      <tbody>
        {reorder.orderedIds.map((id) => (
          <tr key={id} ref={reorder.rowRef(id)}>
            <td>
              <ReorderHandle
                label={`Move ${NAMES[id]}`}
                describedBy="hint"
                handle={reorder.handleProps(id)}
              />
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function names() {
  return screen.getAllByRole("button").map((button) => button.ariaLabel);
}

beforeEach(() => {
  vi.useFakeTimers();
  layOutInDrawnOrder();
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe("dragging a row by its grip", () => {
  it("saves the new order once the row has settled where it was dropped", async () => {
    const onCommit = vi.fn().mockResolvedValue(undefined);
    render(<Rows onCommit={onCommit} />);

    fireEvent.pointerDown(screen.getByRole("button", { name: "Move Movies" }), {
      button: 0,
      clientY: 150,
    });
    fireEvent.pointerMove(window, { clientY: 360 });
    expect(names()).toEqual(["Move TV", "Move Kids", "Move Movies"]);
    expect(onCommit).not.toHaveBeenCalled();
    fireEvent.pointerUp(window);
    await act(async () => {
      vi.advanceTimersByTime(SETTLE_MS);
    });

    expect(onCommit).toHaveBeenCalledWith([2, 3, 1]);
  });

  it("puts the row back on Escape and saves nothing", () => {
    const onCommit = vi.fn().mockResolvedValue(undefined);
    render(<Rows onCommit={onCommit} />);

    fireEvent.pointerDown(screen.getByRole("button", { name: "Move Movies" }), {
      button: 0,
      clientY: 150,
    });
    fireEvent.pointerMove(window, { clientY: 360 });
    fireEvent.keyDown(window, { key: "Escape" });

    expect(names()).toEqual(["Move Movies", "Move TV", "Move Kids"]);
    expect(onCommit).not.toHaveBeenCalled();
  });

  it("leaves the grip neither focused nor pressed after a drop with the pointer", async () => {
    render(<Rows onCommit={vi.fn().mockResolvedValue(undefined)} />);
    screen.getByRole("button", { name: "Move Movies" }).focus();

    fireEvent.pointerDown(screen.getByRole("button", { name: "Move Movies" }), {
      button: 0,
      clientY: 150,
    });
    fireEvent.pointerMove(window, { clientY: 360 });
    fireEvent.pointerUp(window);
    await act(async () => {
      vi.advanceTimersByTime(SETTLE_MS);
    });

    const grip = screen.getByRole("button", { name: "Move Movies" });
    expect(grip).not.toHaveFocus();
    expect(grip).toHaveAttribute("aria-pressed", "false");
  });

  it("does not take focus when it is pressed", () => {
    render(<Rows onCommit={vi.fn().mockResolvedValue(undefined)} />);

    fireEvent.pointerDown(screen.getByRole("button", { name: "Move Movies" }), {
      button: 0,
      clientY: 150,
    });

    expect(
      screen.getByRole("button", { name: "Move Movies" }),
    ).not.toHaveFocus();
  });
});
