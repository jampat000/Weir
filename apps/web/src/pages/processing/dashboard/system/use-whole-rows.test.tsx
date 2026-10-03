import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { useWholeRows } from "./use-whole-rows";

const TILE_PX = 42;
const GAP_PX = 5;

function Tiles({ tiles }: { tiles: number }) {
  const { roomRef, gridRef, height, hidden } = useWholeRows(
    tiles,
    TILE_PX,
    GAP_PX,
  );
  return (
    <div ref={roomRef} data-testid="room">
      <div ref={gridRef} data-testid="grid" style={{ height }} />
      <p data-testid="hidden">{hidden}</p>
    </div>
  );
}

/** A room `roomPx` tall whose grid has `columns` columns, in an environment that measures neither. */
function measuring(roomPx: number, columns: number) {
  vi.spyOn(HTMLElement.prototype, "clientHeight", "get").mockReturnValue(
    roomPx,
  );
  vi.spyOn(window, "getComputedStyle").mockReturnValue({
    gridTemplateColumns: Array(columns).fill("80px").join(" "),
  } as CSSStyleDeclaration);
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe("the tiles' rows", () => {
  it("makes the grid as tall as the whole rows its room holds", () => {
    measuring(100, 2);
    render(<Tiles tiles={10} />);

    expect(screen.getByTestId("grid").style.height).toBe("89px");
  });

  it("counts the tiles left beyond those rows", () => {
    measuring(100, 2);
    render(<Tiles tiles={10} />);

    expect(screen.getByTestId("hidden")).toHaveTextContent("6");
  });

  it("has none beyond when they all fit", () => {
    measuring(200, 2);
    render(<Tiles tiles={6} />);

    expect(screen.getByTestId("hidden")).toHaveTextContent("0");
  });

  it("keeps one row however little room there is", () => {
    measuring(10, 1);
    render(<Tiles tiles={4} />);

    expect(screen.getByTestId("grid").style.height).toBe("42px");
    expect(screen.getByTestId("hidden")).toHaveTextContent("3");
  });
});
