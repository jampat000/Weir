import { FileName } from "../../components/shared/file-name";
import { formatBytes } from "../../lib/format/bytes";
import { baseName } from "../../lib/format/path";
import type { LibraryFile } from "../../lib/processing/library-mode-api";
import { plural } from "../../lib/ui/mm-plural";
import { verdictOf } from "./library-model";

const MANAGER_NAMES: Record<string, string> = {
  sonarr: "Sonarr",
  radarr: "Radarr",
};

function groupMeta(rows: LibraryFile[]): string {
  const manager = rows[0]?.manager_kind;
  const changing = rows.filter(
    (row) => row.classification === "would_change",
  ).length;
  return [
    manager ? `${MANAGER_NAMES[manager] ?? manager} · ` : "",
    plural(rows.length, "file", "files"),
    changing > 0 ? ` · ${changing} would change` : "",
  ].join("");
}

function HeadRow() {
  return (
    <div role="row" className="mm-library-row mm-library-row--head">
      {/* The cell stays in the grid and only its word is hidden: an sr-only cell leaves the grid and
          slides every heading one column left of its values. */}
      <span role="columnheader">
        <span className="sr-only">Select</span>
      </span>
      <span role="columnheader">File</span>
      <span role="columnheader">What Weir would do</span>
      <span role="columnheader">Audio</span>
      <span role="columnheader">Subtitles</span>
      <span role="columnheader" className="mm-library-num">
        Size
      </span>
      <span role="columnheader" className="mm-library-num">
        Back
      </span>
    </div>
  );
}

function FileRow({
  file,
  open,
  selected,
  onToggle,
  onOpen,
}: {
  file: LibraryFile;
  open: boolean;
  selected: boolean;
  onToggle: (path: string) => void;
  onOpen: (path: string) => void;
}) {
  return (
    <div
      role="row"
      className={`mm-library-row mm-library-row--${file.classification}${
        open ? " mm-library-row--open" : ""
      }`}
      data-testid="library-row"
    >
      <span role="cell">
        <input
          type="checkbox"
          className="mm-library-check"
          aria-label={`Select ${baseName(file.path)}`}
          checked={selected}
          disabled={file.classification !== "would_change"}
          onChange={() => onToggle(file.path)}
        />
      </span>
      <span role="cell" className="mm-library-name">
        <button type="button" onClick={() => onOpen(file.path)}>
          <FileName path={file.path} />
        </button>
      </span>
      <span role="cell" className="mm-library-verdict">
        {verdictOf(file)}
      </span>
      <span role="cell" className="mm-library-tracks">
        {file.audio_summary ?? `${file.audio_track_count}`}
      </span>
      <span role="cell" className="mm-library-tracks">
        {file.subtitle_summary ?? `${file.subtitle_track_count}`}
      </span>
      <span role="cell" className="mm-library-num">
        {formatBytes(file.size_bytes)}
      </span>
      <span role="cell" className="mm-library-num">
        {file.estimated_bytes_saved > 0
          ? formatBytes(file.estimated_bytes_saved)
          : "—"}
      </span>
    </div>
  );
}

/** The library's files grouped by title, one row each, with a note when the server sent only the first page. */
export function LibraryTable({
  libraryName,
  groups,
  compact,
  openPath,
  selected,
  shownCount,
  total,
  onToggle,
  onOpen,
}: {
  libraryName: string;
  groups: [string, LibraryFile[]][];
  compact: boolean;
  openPath: string | null;
  selected: Set<string>;
  shownCount: number;
  total: number;
  onToggle: (path: string) => void;
  onOpen: (path: string) => void;
}) {
  return (
    <div
      className={`mm-library-table${compact ? " mm-library-table--compact" : ""}`}
      role="table"
      aria-label={`Files in ${libraryName}`}
    >
      <HeadRow />
      {groups.map(([title, rows]) => (
        <div key={title} role="rowgroup" className="mm-library-group">
          <div role="row" className="mm-library-grouprow">
            <span role="cell" className="mm-library-grouprow__title">
              {title}
            </span>
            <span role="cell" className="mm-library-grouprow__meta">
              {groupMeta(rows)}
            </span>
          </div>
          {rows.map((file) => (
            <FileRow
              key={file.path}
              file={file}
              open={openPath === file.path}
              selected={selected.has(file.path)}
              onToggle={onToggle}
              onOpen={onOpen}
            />
          ))}
        </div>
      ))}
      {total > shownCount ? (
        <p className="mm-library-more">
          Showing {shownCount.toLocaleString()} of {total.toLocaleString()}.
          Narrow it with search or a chip to see the rest.
        </p>
      ) : null}
    </div>
  );
}
