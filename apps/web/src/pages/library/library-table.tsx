import type { CSSProperties } from "react";

import { SortableColumnHeader } from "../../components/shared/sortable-column-header";
import type { LibraryFile } from "../../lib/processing/library-mode-api";
import type { TableColumns } from "../../lib/ui/use-table-columns";
import { SingleFileRow } from "./library-file-row";
import {
  LIBRARY_CELL_CLASS,
  libraryGrid,
  type LibraryColumnId,
} from "./library-columns";
import { LibraryGroup } from "./library-group";

function HeadRow({ columns }: { columns: TableColumns<LibraryColumnId> }) {
  return (
    <div role="row" className="mm-library-row mm-library-row--head">
      {columns.order.map((id) => (
        <SortableColumnHeader
          key={id}
          as="div"
          heading={columns.heading(id)}
          className={LIBRARY_CELL_CLASS[id]}
          hideLabel={id === "select"}
        />
      ))}
    </div>
  );
}

/** One page of the library's files by title: a title with one file is a single row, one with several nests them. */
export function LibraryTable({
  libraryName,
  groups,
  columns,
  openPath,
  selected,
  onToggle,
  onOpen,
}: {
  libraryName: string;
  groups: [string, LibraryFile[]][];
  columns: TableColumns<LibraryColumnId>;
  openPath: string | null;
  selected: Set<string>;
  onToggle: (path: string) => void;
  onOpen: (path: string) => void;
}) {
  const { order } = columns;
  return (
    <div
      {...columns.tableProps}
      className="mm-library-table"
      role="table"
      aria-label={`Files in ${libraryName}`}
      style={libraryGrid(order) as CSSProperties}
    >
      <HeadRow columns={columns} />
      {groups.map(([title, rows]) =>
        rows.length === 1 ? (
          <div key={title} role="rowgroup" className="mm-library-group">
            <SingleFileRow
              file={rows[0]}
              title={title}
              libraryName={libraryName}
              order={order}
              open={openPath === rows[0].path}
              selected={selected.has(rows[0].path)}
              onToggle={onToggle}
              onOpen={onOpen}
            />
          </div>
        ) : (
          <LibraryGroup
            key={title}
            title={title}
            rows={rows}
            libraryName={libraryName}
            order={order}
            openPath={openPath}
            selected={selected}
            onToggle={onToggle}
            onOpen={onOpen}
          />
        ),
      )}
    </div>
  );
}
