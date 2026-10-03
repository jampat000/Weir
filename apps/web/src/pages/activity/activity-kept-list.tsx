import { Fragment, useState, type ReactNode } from "react";

import { FileName } from "../../components/shared/file-name";
import { SortableColumnHeader } from "../../components/shared/sortable-column-header";
import { errorMessage } from "../../lib/api/error-message";
import { formatBytes } from "../../lib/format/bytes";
import type { KeptFile } from "../../lib/processing/kept-files-api";
import { useProcessKeptFileAgain } from "../../lib/processing/kept-files-queries";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import { sortRows } from "../../lib/ui/table-columns";
import type { TableColumns } from "../../lib/ui/use-table-columns";
import type { KeptColumnId } from "./activity-columns";

/** What each column that sorts sorts by: the file's path, its workflow's name, and its size in bytes. */
const KEPT_SORT_VALUES = {
  file: (file: KeptFile) => file.relative_path,
  workflow: (file: KeptFile) => file.library_name,
  size: (file: KeptFile) => file.size_bytes,
};

/** One kept file's row: what it is, where it is, and the one way back. */
function KeptFileRow({
  file,
  order,
  editable,
  onProcessed,
}: {
  file: KeptFile;
  order: readonly KeptColumnId[];
  editable: boolean;
  onProcessed: (message: string) => void;
}) {
  const processAgain = useProcessKeptFileAgain();
  const [failure, setFailure] = useState<string | null>(null);

  const cells: Record<KeptColumnId, ReactNode> = {
    file: (
      <td data-col="file">
        <FileName path={file.relative_path} className="mm-history-file__name" />
      </td>
    ),
    workflow: <td data-col="workflow">{file.library_name}</td>,
    size: <td data-col="size">{formatBytes(file.size_bytes)}</td>,
    actions: (
      <td data-col="actions">
        {editable ? (
          <button
            type="button"
            className={mmActionButtonClass({ variant: "secondary" })}
            disabled={processAgain.isPending}
            onClick={() => {
              setFailure(null);
              processAgain.mutate(file.id, {
                onSuccess: (result) => onProcessed(result.detail),
                onError: (error) =>
                  setFailure(
                    errorMessage(
                      error,
                      "That file could not be processed again.",
                    ),
                  ),
              });
            }}
          >
            {processAgain.isPending ? "Queueing…" : "Process again"}
          </button>
        ) : null}
        {failure ? (
          <p
            className="mm-status-text mt-1 text-sm"
            data-status="broken"
            role="alert"
          >
            {failure}
          </p>
        ) : null}
      </td>
    ),
  };

  return (
    <tr data-testid={`kept-file-row-${file.id}`}>
      {order.map((id) => (
        <Fragment key={id}>{cells[id]}</Fragment>
      ))}
    </tr>
  );
}

/**
 * The files someone chose to keep without processing again from Activity's remove dialog (#786 review of #785),
 * listed under Activity's "Kept" chip since a title someone removed from the rest of the list is where they will
 * look for it. "Process again" clears the marker and asks Weir to look at its library now, exactly as if the file
 * had just appeared.
 */
export function ActivityKeptList({
  files,
  columns,
  editable,
  onProcessed,
}: {
  files: KeptFile[];
  columns: TableColumns<KeptColumnId>;
  editable: boolean;
  onProcessed: (message: string) => void;
}) {
  if (files.length === 0) {
    return <p className="mm-history-empty">No files are kept right now.</p>;
  }
  return (
    <table
      className="mm-history-table"
      data-testid="kept-files-table"
      {...columns.tableProps}
    >
      <thead>
        <tr>
          {columns.order.map((id) => (
            <SortableColumnHeader
              key={id}
              heading={columns.heading(id)}
              hideLabel={id === "actions"}
            />
          ))}
        </tr>
      </thead>
      <tbody>
        {sortRows(files, columns.sort, KEPT_SORT_VALUES).map((file) => (
          <KeptFileRow
            key={file.id}
            file={file}
            order={columns.order}
            editable={editable}
            onProcessed={onProcessed}
          />
        ))}
      </tbody>
    </table>
  );
}
