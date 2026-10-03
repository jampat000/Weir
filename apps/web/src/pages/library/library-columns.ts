import type { LibraryFileSort } from "../../lib/processing/library-mode-api";
import type { TableColumnsConfig } from "../../lib/ui/table-columns";

export type LibraryColumnId =
  "select" | "title" | "status" | "audio" | "subtitles" | "size" | "saved";

/**
 * The Files table's columns, in the order they start as. The box that selects a file keeps the first place; the rest can
 * be moved, and every one but it sorts, by the server, so a sort covers the whole library and not the page on screen.
 */
export const LIBRARY_COLUMNS: TableColumnsConfig<LibraryColumnId> = {
  tableId: "library-files",
  sortable: true,
  defaultSort: { id: "title", direction: "asc" },
  columns: [
    { id: "select", label: "Select", movable: false, sortable: false },
    { id: "title", label: "Title" },
    { id: "status", label: "What Weir would do" },
    { id: "audio", label: "Audio" },
    { id: "subtitles", label: "Subtitles" },
    { id: "size", label: "Size", firstDirection: "desc" },
    { id: "saved", label: "Back", firstDirection: "desc" },
  ],
};

/** What the server calls the sort for each column that sorts. */
export const LIBRARY_SORT_KEYS: Record<
  Exclude<LibraryColumnId, "select">,
  LibraryFileSort
> = {
  title: "title",
  status: "status",
  audio: "audio",
  subtitles: "subtitles",
  size: "size",
  saved: "saved",
};

/** The classes that line a heading up with the cells under it, and that hide a column when the table is narrow. */
export const LIBRARY_CELL_CLASS: Record<LibraryColumnId, string | undefined> = {
  select: undefined,
  title: "mm-library-identity",
  status: "mm-library-verdict",
  audio: "mm-library-tracks",
  subtitles: "mm-library-tracks",
  size: "mm-library-num",
  saved: "mm-library-num mm-library-back",
};

/** Where each column's track sits in the table's grid, and how much room it takes. */
const TRACKS: Record<LibraryColumnId, string> = {
  select: "var(--lib-check)",
  title: "minmax(0, 3.2fr)",
  status: "minmax(0, 2fr)",
  audio: "minmax(0, 1.2fr)",
  subtitles: "minmax(0, 1.2fr)",
  size: "5.5rem",
  saved: "5rem",
};

/** The columns that give way when the table is narrow: the track counts and what cleaning would give back. */
export const NARROW_HIDDEN: ReadonlySet<LibraryColumnId> = new Set([
  "audio",
  "subtitles",
  "saved",
]);

/** The grid's columns for the order, with every column and with only those that stay when the table is narrow. */
export function libraryGrid(order: readonly LibraryColumnId[]): {
  "--lib-cols": string;
  "--lib-cols-narrow": string;
} {
  const tracks = (ids: readonly LibraryColumnId[]) =>
    ids.map((id) => TRACKS[id]).join(" ");
  return {
    "--lib-cols": tracks(order),
    "--lib-cols-narrow": tracks(order.filter((id) => !NARROW_HIDDEN.has(id))),
  };
}
