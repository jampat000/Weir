import type { LibraryFile } from "../../lib/processing/library-mode-api";
import { SingleFileRow } from "./library-file-row";
import { LibraryGroup } from "./library-group";

function HeadRow() {
  return (
    <div role="row" className="mm-library-row mm-library-row--head">
      {/* The cell stays in the grid and only its word is hidden: an sr-only cell leaves the grid and
          slides every heading one column left of its values. */}
      <span role="columnheader">
        <span className="sr-only">Select</span>
      </span>
      <span role="columnheader" className="mm-library-identity">
        Title
      </span>
      <span role="columnheader" className="mm-library-verdict">
        What Weir would do
      </span>
      <span role="columnheader" className="mm-library-tracks">
        Audio
      </span>
      <span role="columnheader" className="mm-library-tracks">
        Subtitles
      </span>
      <span role="columnheader" className="mm-library-num">
        Size
      </span>
      <span role="columnheader" className="mm-library-num mm-library-back">
        Back
      </span>
    </div>
  );
}

/** One page of the library's files by title: a title with one file is a single row, one with several nests them. */
export function LibraryTable({
  libraryName,
  groups,
  compact,
  openPath,
  selected,
  onToggle,
  onOpen,
}: {
  libraryName: string;
  groups: [string, LibraryFile[]][];
  compact: boolean;
  openPath: string | null;
  selected: Set<string>;
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
      {groups.map(([title, rows]) =>
        rows.length === 1 ? (
          <div key={title} role="rowgroup" className="mm-library-group">
            <SingleFileRow
              file={rows[0]}
              title={title}
              libraryName={libraryName}
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
