import type { TableColumnsConfig } from "../../../../lib/ui/table-columns";

export type BackupColumnId = "taken" | "size" | "actions";

/** The backups in the folder: few enough to be all loaded, so the browser sorts them, newest first until told otherwise. */
export const BACKUP_COLUMNS: TableColumnsConfig<BackupColumnId> = {
  tableId: "system-backups",
  sortable: true,
  defaultSort: { id: "taken", direction: "desc" },
  columns: [
    { id: "taken", label: "Taken", firstDirection: "desc" },
    { id: "size", label: "Size", firstDirection: "desc" },
    { id: "actions", label: "Actions", movable: false, sortable: false },
  ],
};
