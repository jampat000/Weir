import { MoreMenu } from "../shell/more-menu";
import type { TableColumns } from "../../lib/ui/use-table-columns";

const RESET = "reset";

const MOVE_HINT =
  "Drag a heading to move its column, or press Alt with the left or right arrow on it.";
const SORT_HINT = " Click a heading to sort by it, and click again to reverse.";

/**
 * The small menu on a list table's card header, with the one thing it offers: putting the table's columns and sort back
 * as they started. It also carries what a screen reader is told about moving and sorting columns, which every heading
 * points at, and what it is told after each move.
 */
export function ColumnsMenu<Id extends string>({
  table,
}: {
  table: TableColumns<Id>;
}) {
  return (
    <>
      <MoreMenu
        label="Columns"
        align="end"
        menuLabel="Columns"
        folded={[{ id: RESET, label: "Reset columns" }]}
        onChoose={table.reset}
        buttonClassName="mm-columns-button"
      />
      <p id={table.hintId} className="sr-only">
        {MOVE_HINT}
        {table.sortable ? SORT_HINT : ""}
      </p>
      <p role="status" className="sr-only">
        {table.announcement}
      </p>
    </>
  );
}
