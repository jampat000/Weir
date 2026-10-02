import type { ReactNode } from "react";

import type { ColumnHeading } from "../../lib/ui/use-table-columns";
import { classNames } from "../../lib/ui/class-names";

const ARROW_UP = "M5 8.5v-6M2.5 5 5 2.5 7.5 5";
const ARROW_DOWN = "M5 1.5v6M2.5 5 5 7.5 7.5 5";

function SortArrow({ direction }: { direction: "asc" | "desc" }) {
  return (
    <svg
      className="mm-col-head__arrow"
      viewBox="0 0 10 10"
      width="10"
      height="10"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d={direction === "asc" ? ARROW_UP : ARROW_DOWN} />
    </svg>
  );
}

/**
 * One column's heading. Clicking it sorts the table by the column and clicking again reverses, with an arrow and
 * `aria-sort` saying which; dragging it moves the column, and Alt with an arrow key moves it from the keyboard. A column
 * that cannot sort is a plain heading, and one that cannot move is not dragged. See `useTableColumns`.
 */
export function SortableColumnHeader<Id extends string>({
  heading,
  as = "th",
  className,
  hideLabel = false,
  children,
}: {
  heading: ColumnHeading<Id>;
  /** `div` for a grid of role-based cells, which has no `th`. */
  as?: "th" | "div";
  className?: string;
  /** Says the column's name to a screen reader only, for a column of checkboxes or buttons. */
  hideLabel?: boolean;
  /** Draws the heading's words instead of the column's label, which is still what is announced. */
  children?: ReactNode;
}) {
  const { column, sorted, sortable, movable } = heading;
  const Tag = as;
  const words = hideLabel ? (
    <span className="sr-only">{column.label}</span>
  ) : (
    (children ?? column.label)
  );
  const hint = movable ? heading.hintId : undefined;
  return (
    <Tag
      role={as === "div" ? "columnheader" : undefined}
      scope={as === "th" ? "col" : undefined}
      aria-sort={
        sorted ? (sorted === "asc" ? "ascending" : "descending") : undefined
      }
      data-col={column.id}
      data-movable={movable || undefined}
      className={classNames("mm-col-head", className)}
      onPointerDown={heading.onPointerDown}
      onKeyDown={heading.onKeyDown}
      tabIndex={!sortable && movable ? 0 : undefined}
      aria-describedby={sortable ? undefined : hint}
    >
      {sortable ? (
        <button
          type="button"
          className="mm-col-head__sort"
          aria-describedby={hint}
          onClick={heading.onSort}
        >
          {words}
          {sorted ? <SortArrow direction={sorted} /> : null}
        </button>
      ) : (
        words
      )}
    </Tag>
  );
}
