import type { ReorderHandleProps } from "./use-row-reorder";

/** The grip a row is dragged by, or moved with from the keyboard; see `useRowReorder`. */
export function ReorderHandle({
  label,
  describedBy,
  handle,
}: {
  label: string;
  /** The id of the text that says how to use it, for a screen reader. */
  describedBy: string;
  handle: ReorderHandleProps;
}) {
  return (
    <button
      type="button"
      className="mm-reorder-handle"
      aria-label={label}
      aria-describedby={describedBy}
      {...handle}
    >
      <svg viewBox="0 0 10 16" width="10" height="16" aria-hidden="true">
        <circle cx="2.5" cy="3" r="1.4" />
        <circle cx="7.5" cy="3" r="1.4" />
        <circle cx="2.5" cy="8" r="1.4" />
        <circle cx="7.5" cy="8" r="1.4" />
        <circle cx="2.5" cy="13" r="1.4" />
        <circle cx="7.5" cy="13" r="1.4" />
      </svg>
    </button>
  );
}
