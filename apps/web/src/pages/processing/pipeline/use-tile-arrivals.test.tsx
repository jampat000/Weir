import { render } from "@testing-library/react";
import { useRef } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { HELD_ATTRIBUTE } from "./delivery-flight";
import { TILE_KEY_ATTRIBUTE } from "./use-slide-neighbours";
import { RESIZING_CLASS } from "./use-still-while-resizing";
import { useTileArrivals } from "./use-tile-arrivals";

type Tile = { key: string; held?: boolean };

function Row({ filed, shown }: { filed: string; shown: readonly Tile[] }) {
  const row = useRef<HTMLUListElement>(null);
  useTileArrivals(row, filed);
  return (
    <ul ref={row}>
      {shown.map((tile) => (
        <li key={tile.key} {...{ [TILE_KEY_ATTRIBUTE]: tile.key }}>
          <span {...(tile.held ? { [HELD_ATTRIBUTE]: "" } : {})} />
        </li>
      ))}
    </ul>
  );
}

const keysOf = (tiles: readonly Tile[]) =>
  tiles.map((tile) => tile.key).join("|");

let animated: string[];

beforeEach(() => {
  animated = [];
  Element.prototype.animate = vi.fn(function (this: Element) {
    animated.push(this.getAttribute(TILE_KEY_ATTRIBUTE) ?? "");
    return {} as Animation;
  });
});

afterEach(() => {
  Reflect.deleteProperty(Element.prototype, "animate");
  document.body.classList.remove(RESIZING_CLASS);
  vi.restoreAllMocks();
});

describe("a tile arriving on the shelf", () => {
  it("arrives when it is new to the shelf, and only that tile", () => {
    const first: Tile[] = [{ key: "1" }, { key: "2" }];
    const { rerender } = render(<Row filed={keysOf(first)} shown={first} />);
    animated.length = 0;

    const next: Tile[] = [{ key: "3" }, ...first];
    rerender(<Row filed={keysOf(next)} shown={next} />);

    expect(animated).toEqual(["3"]);
  });

  it("does not arrive again when the shelf only now has room for it", () => {
    const all: Tile[] = [{ key: "1" }, { key: "2" }, { key: "3" }];
    const { rerender } = render(
      <Row filed={keysOf(all)} shown={all.slice(0, 2)} />,
    );
    animated.length = 0;

    // The window grew: the third tile is shown, and it was already one of the shelf's.
    rerender(<Row filed={keysOf(all)} shown={all} />);

    expect(animated).toEqual([]);
  });

  it("has no arrival for a tile held for a poster that is about to fly to it", () => {
    const first: Tile[] = [{ key: "1" }];
    const { rerender } = render(<Row filed={keysOf(first)} shown={first} />);
    animated.length = 0;

    const next: Tile[] = [{ key: "2", held: true }, ...first];
    rerender(<Row filed={keysOf(next)} shown={next} />);

    expect(animated).toEqual([]);
  });

  it("does not arrive while the window is being resized", () => {
    const first: Tile[] = [{ key: "1" }];
    const { rerender } = render(<Row filed={keysOf(first)} shown={first} />);
    animated.length = 0;
    document.body.classList.add(RESIZING_CLASS);

    const next: Tile[] = [{ key: "2" }, ...first];
    rerender(<Row filed={keysOf(next)} shown={next} />);

    expect(animated).toEqual([]);
  });
});
