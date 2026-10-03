/**
 * The workflow chip this browser last chose on the Just finished shelf, so the shelf opens the way it was left.
 * Like the compact-rows choice in the Library, it belongs to the browser, not to Weir.
 */
const SHELF_WORKFLOW_KEY = "weir.live.shelfWorkflow";
const ALL_WORKFLOWS = "all";

/** The id of the last workflow chosen here, or null for all of them, or when the browser will not say. */
export function readShelfWorkflow(): number | null {
  try {
    const stored = Number(localStorage.getItem(SHELF_WORKFLOW_KEY));
    return Number.isInteger(stored) && stored > 0 ? stored : null;
  } catch {
    return null;
  }
}

export function saveShelfWorkflow(workflowId: number | null): void {
  try {
    localStorage.setItem(
      SHELF_WORKFLOW_KEY,
      workflowId === null ? ALL_WORKFLOWS : String(workflowId),
    );
  } catch {
    // A browser that will not remember the choice is no reason to refuse it.
  }
}
