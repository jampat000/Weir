import { Fragment, type ReactNode } from "react";

import { SortableColumnHeader } from "../../../../components/shared/sortable-column-header";
import { formatBytes } from "../../../../lib/format/bytes";
import type { ConfigurationBackupItem } from "../../../../lib/settings/types";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import {
  parseAppTime,
  useAppDateFormatter,
} from "../../../../lib/ui/mm-format-date";
import { sortRows } from "../../../../lib/ui/table-columns";
import type { TableColumns } from "../../../../lib/ui/use-table-columns";
import type { BackupColumnId } from "./backup-columns";
import { DownloadIcon, RestoreIcon } from "./backup-icons";

const BACKUP_SORT_VALUES = {
  taken: (row: ConfigurationBackupItem) => parseAppTime(row.created_at),
  size: (row: ConfigurationBackupItem) => row.size_bytes,
};

const ICON_BUTTON = `${mmActionButtonClass({ variant: "secondary" })} mm-sys-btn mm-sys-btn--icon`;

function BackupRow({
  row,
  order,
  disabled,
  onDownload,
  onRestore,
}: {
  row: ConfigurationBackupItem;
  order: readonly BackupColumnId[];
  disabled: boolean;
  onDownload: (id: number, fileName: string) => void;
  onRestore: (id: number) => void;
}) {
  const formatDate = useAppDateFormatter();
  const taken = formatDate(row.created_at);
  const cells: Record<BackupColumnId, ReactNode> = {
    taken: (
      <th scope="row" data-col="taken">
        {taken}
      </th>
    ),
    size: <td data-col="size">{formatBytes(row.size_bytes)}</td>,
    actions: (
      <td data-col="actions">
        <div className="mm-sys-table__actions">
          <button
            type="button"
            className={ICON_BUTTON}
            disabled={disabled}
            title="Download this backup"
            aria-label={`Download the backup taken ${taken}`}
            onClick={() => onDownload(row.id, row.file_name)}
          >
            <DownloadIcon />
          </button>
          <button
            type="button"
            className={ICON_BUTTON}
            disabled={disabled}
            title="Restore this backup"
            aria-label={`Restore the backup taken ${taken}`}
            onClick={() => onRestore(row.id)}
          >
            <RestoreIcon />
          </button>
        </div>
      </td>
    ),
  };
  return (
    <tr>
      {order.map((id) => (
        <Fragment key={id}>{cells[id]}</Fragment>
      ))}
    </tr>
  );
}

/** The backups as a table whose headings sort it and whose columns can be moved. */
export function BackupTable({
  items,
  columns,
  disabled,
  onDownload,
  onRestore,
}: {
  items: readonly ConfigurationBackupItem[];
  columns: TableColumns<BackupColumnId>;
  disabled: boolean;
  onDownload: (id: number, fileName: string) => void;
  onRestore: (id: number) => void;
}) {
  return (
    <table className="mm-quiet-table mm-sys-table" {...columns.tableProps}>
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
        {sortRows(items, columns.sort, BACKUP_SORT_VALUES).map((row) => (
          <BackupRow
            key={row.id}
            row={row}
            order={columns.order}
            disabled={disabled}
            onDownload={onDownload}
            onRestore={onRestore}
          />
        ))}
      </tbody>
    </table>
  );
}
