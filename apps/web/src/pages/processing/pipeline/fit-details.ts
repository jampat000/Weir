/**
 * Fits each card's detail block to its height: after the cards are placed, a card shows as many whole
 * lines as its height holds and no more, so a short card shows none and a tall one several, and nothing
 * is ever cut through a line.
 */
import { detailRoom } from "./pipeline-layout";

export const CARD_ATTRIBUTE = "data-pipeline-card";
export const DETAILS_ATTRIBUTE = "data-pipeline-details";

export function fitDetails(host: HTMLElement): void {
  for (const card of host.querySelectorAll<HTMLElement>(
    `[${CARD_ATTRIBUTE}]`,
  )) {
    const block = card.querySelector<HTMLElement>(`[${DETAILS_ATTRIBUTE}]`);
    if (!block) continue;
    const lines = Array.from(block.children) as HTMLElement[];
    block.style.display = "";
    for (const line of lines) line.style.display = "";
    const room = detailRoom(
      card.getBoundingClientRect().bottom,
      block.getBoundingClientRect().top,
    );
    lines.forEach((line, index) => {
      line.style.display = index < room ? "" : "none";
    });
    // With no line to show the block is gone, margin and all, so a short card has nothing under its bar.
    block.style.display = room > 0 ? "" : "none";
  }
}
