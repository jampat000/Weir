/** The Needs you panel's id, so another part of the Dashboard can take the person to it. */
export const NEEDS_PANEL_ID = "needs-you";

/** Scrolls the Needs you panel into view and moves focus to it, so a keyboard follows. */
export function showNeedsPanel(): void {
  const panel = document.getElementById(NEEDS_PANEL_ID);
  panel?.scrollIntoView({ block: "start" });
  panel?.focus({ preventScroll: true });
}
