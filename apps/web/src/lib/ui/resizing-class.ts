/**
 * The class the document carries while its window is being resized; the Dashboard's stylesheet gives every
 * animation and transition under it no time (weir-processing-dashboard.css), and drawn motion checks it too.
 */
export const RESIZING_CLASS = "mm-pipeline-resizing";

/** Whether the window is being resized right now. */
export function windowIsResizing(): boolean {
  return (
    typeof document !== "undefined" &&
    document.body.classList.contains(RESIZING_CLASS)
  );
}
