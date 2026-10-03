/**
 * The library this browser last picked, so opening Library without `?library=` lands where the person
 * was. Like the column layout, it belongs to the browser, not to Weir.
 */
const LAST_LIBRARY_KEY = "weir-library-last";

/** The id of the last library picked here, or null when none was or the browser will not say. */
export function readLastLibrary(): number | null {
  try {
    const stored = Number(localStorage.getItem(LAST_LIBRARY_KEY));
    return Number.isInteger(stored) && stored > 0 ? stored : null;
  } catch {
    return null;
  }
}

export function saveLastLibrary(id: number): void {
  try {
    localStorage.setItem(LAST_LIBRARY_KEY, String(id));
  } catch {
    // A browser that will not remember the choice is no reason to refuse it.
  }
}
