/**
 * Fits each card's detail block to its height: after the cards are placed, a card shows as many whole
 * lines as its height holds and no more, so a short card shows none and a tall one several, and nothing
 * is ever cut through a line. A card that must keep its first lines (a stopped file's reason) gives up
 * the second line of its title to make room for them before it gives them up.
 */
import { detailRoom } from "./pipeline-layout";

export const CARD_ATTRIBUTE = "data-pipeline-card";
export const DETAILS_ATTRIBUTE = "data-pipeline-details";
/** On the detail block: how many of its first lines the card never drops. */
export const KEEP_ATTRIBUTE = "data-pipeline-keep";
/** On the card: its title is one line, so its detail lines have room. */
export const TIGHT_ATTRIBUTE = "data-pipeline-tight";

function roomIn(card: HTMLElement, block: HTMLElement): number {
  return detailRoom(
    card.getBoundingClientRect().bottom,
    block.getBoundingClientRect().top,
  );
}

/** How many whole detail lines the card has room for, after giving up a line of its title if it must keep some. */
function roomFor(card: HTMLElement, block: HTMLElement): number {
  const keep = Number(block.getAttribute(KEEP_ATTRIBUTE)) || 0;
  card.removeAttribute(TIGHT_ATTRIBUTE);
  const room = roomIn(card, block);
  if (room >= keep) return room;
  card.setAttribute(TIGHT_ATTRIBUTE, "");
  const tight = roomIn(card, block);
  if (tight >= keep) return tight;
  card.removeAttribute(TIGHT_ATTRIBUTE);
  return room;
}

export function fitDetails(host: HTMLElement): void {
  for (const card of host.querySelectorAll<HTMLElement>(
    `[${CARD_ATTRIBUTE}]`,
  )) {
    const block = card.querySelector<HTMLElement>(`[${DETAILS_ATTRIBUTE}]`);
    if (!block) continue;
    const lines = Array.from(block.children) as HTMLElement[];
    block.style.display = "";
    for (const line of lines) line.style.display = "";
    const room = roomFor(card, block);
    lines.forEach((line, index) => {
      line.style.display = index < room ? "" : "none";
    });
    // With no line to show the block is gone, margin and all, so a short card has nothing under its bar.
    block.style.display = room > 0 ? "" : "none";
  }
}
