import type { TableColumnsConfig } from "../../lib/ui/table-columns";

export type TrackColumnId = "keep" | "track" | "default" | "forced" | "rule";

/** The column names, also shown beside each value once a narrow screen stacks the rows. */
export const TRACK_COLUMN_LABELS: Record<TrackColumnId, string> = {
  keep: "Keep",
  track: "Track",
  default: "Default",
  forced: "Forced",
  rule: "What the saved rules would do",
};

/**
 * The table's rows are the file's own tracks in the order the file holds them, which is the point of the table, so its
 * headings do not sort; its columns can still be moved.
 */
export const TRACK_COLUMNS: TableColumnsConfig<TrackColumnId> = {
  tableId: "choose-tracks",
  sortable: false,
  defaultSort: null,
  columns: (Object.keys(TRACK_COLUMN_LABELS) as TrackColumnId[]).map((id) => ({
    id,
    label: TRACK_COLUMN_LABELS[id],
  })),
};
