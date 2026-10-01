import { afterEach, describe, expect, it, vi } from "vitest";

import { CARD_ATTRIBUTE, DETAILS_ATTRIBUTE, fitDetails } from "./fit-details";

afterEach(() => vi.restoreAllMocks());

/** A card whose box ends at `cardBottom`, with a block of four detail lines that starts at `blockTop`. */
function cardWithFourLines(cardBottom: number, blockTop: number) {
  const host = document.createElement("div");
  host.innerHTML = `<div ${CARD_ATTRIBUTE}><span ${DETAILS_ATTRIBUTE}>${"<span>line</span>".repeat(4)}</span></div>`;
  const card = host.querySelector<HTMLElement>(`[${CARD_ATTRIBUTE}]`);
  const block = host.querySelector<HTMLElement>(`[${DETAILS_ATTRIBUTE}]`);
  if (!card || !block) throw new Error("the card was not built");
  card.getBoundingClientRect = () => ({ bottom: cardBottom }) as DOMRect;
  block.getBoundingClientRect = () => ({ top: blockTop }) as DOMRect;
  return { block, lines: Array.from(block.children) as HTMLElement[] };
}

describe("fitting a card's detail lines to its height", () => {
  it("shows as many whole lines as the room under the bar holds", () => {
    const { block, lines } = cardWithFourLines(500, 440);

    fitDetails(block.parentElement?.parentElement as HTMLElement);

    expect(lines.map((line) => line.style.display)).toEqual([
      "",
      "",
      "",
      "none",
    ]);
    expect(block.style.display).toBe("");
  });

  it("takes the whole block away, margin and all, when not even one line fits", () => {
    const { block, lines } = cardWithFourLines(419, 400);

    fitDetails(block.parentElement?.parentElement as HTMLElement);

    expect(block.style.display).toBe("none");
    expect(lines.every((line) => line.style.display === "none")).toBe(true);
  });

  it("measures again from scratch each time, so a card that grows gets its lines back", () => {
    const { block, lines } = cardWithFourLines(419, 400);
    const host = block.parentElement?.parentElement as HTMLElement;
    fitDetails(host);

    (host.firstElementChild as HTMLElement).getBoundingClientRect = () =>
      ({ bottom: 520 }) as DOMRect;
    fitDetails(host);

    expect(lines.map((line) => line.style.display)).toEqual(["", "", "", ""]);
    expect(block.style.display).toBe("");
  });
});
