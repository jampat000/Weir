/**
 * Whether the menu was last folded to icons in this browser, so it stays as the person left it. Like the
 * last library picked, it belongs to the browser, not to Weir. Deluno remembers its own the same way.
 */
const COLLAPSED_KEY = "weir.sidebar.collapsed";

/** The last choice made here, or null when none was or the browser will not say. */
export function readCollapsedChoice(): boolean | null {
  try {
    const stored = localStorage.getItem(COLLAPSED_KEY);
    return stored === "true" ? true : stored === "false" ? false : null;
  } catch {
    return null;
  }
}

export function saveCollapsedChoice(collapsed: boolean): void {
  try {
    localStorage.setItem(COLLAPSED_KEY, String(collapsed));
  } catch {
    // A browser that will not remember the choice is no reason to refuse it.
  }
}
