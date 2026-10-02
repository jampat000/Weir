import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { useChipRow } from "./use-chip-row";

const ROW_WIDTH = 200;
const CHIPS_WIDTH = 300;

function Row({ chosen = "first" }: { chosen?: string }) {
  const { setRow, scrolls, more } = useChipRow(chosen);
  return (
    <div
      ref={setRow}
      data-testid="row"
      data-scrolls={scrolls}
      data-before={more.before}
      data-after={more.after}
    >
      <button type="button" aria-pressed="true">
        First
      </button>
    </div>
  );
}

/** Where the chosen chip is in the row: whole, with the row's own padding before it, so the row has no reason to scroll to it. */
const CHOSEN_CHIP = { left: 8, width: 40 };

function rowOfSize(clientWidth: number, scrollWidth: number) {
  vi.spyOn(HTMLElement.prototype, "offsetLeft", "get").mockReturnValue(
    CHOSEN_CHIP.left,
  );
  vi.spyOn(HTMLElement.prototype, "offsetWidth", "get").mockReturnValue(
    CHOSEN_CHIP.width,
  );
  vi.spyOn(Element.prototype, "clientWidth", "get").mockReturnValue(
    clientWidth,
  );
  vi.spyOn(Element.prototype, "scrollWidth", "get").mockReturnValue(
    scrollWidth,
  );
  render(<Row />);
  return screen.getByTestId("row");
}

afterEach(() => vi.restoreAllMocks());

describe("a row of chips wider than its room", () => {
  it("says it scrolls, with chips past its end and none before its start", () => {
    const row = rowOfSize(ROW_WIDTH, CHIPS_WIDTH);

    expect(row).toHaveAttribute("data-scrolls", "true");
    expect(row).toHaveAttribute("data-before", "false");
    expect(row).toHaveAttribute("data-after", "true");
  });

  it("says there are chips before it once it has scrolled, and none past the end once it reaches it", () => {
    const row = rowOfSize(ROW_WIDTH, CHIPS_WIDTH);

    row.scrollLeft = 40;
    act(() => {
      fireEvent.scroll(row);
    });
    expect(row).toHaveAttribute("data-before", "true");
    expect(row).toHaveAttribute("data-after", "true");

    row.scrollLeft = CHIPS_WIDTH - ROW_WIDTH;
    act(() => {
      fireEvent.scroll(row);
    });
    expect(row).toHaveAttribute("data-after", "false");
  });

  it("turns a mouse wheel's up and down into sideways scrolling", () => {
    const row = rowOfSize(ROW_WIDTH, CHIPS_WIDTH);

    const turned = !fireEvent.wheel(row, { deltaY: 30 });

    expect(row.scrollLeft).toBe(30);
    expect(turned).toBe(true);
  });
});

describe("a row of chips that all fit", () => {
  it("has nothing to scroll to, and leaves the wheel to the page", () => {
    const row = rowOfSize(CHIPS_WIDTH, CHIPS_WIDTH);

    const turned = !fireEvent.wheel(row, { deltaY: 30 });

    expect(row).toHaveAttribute("data-scrolls", "false");
    expect(row).toHaveAttribute("data-after", "false");
    expect(turned).toBe(false);
  });
});
