import { useState } from "react";

import { Poster } from "../../components/shared/poster";
import { NavIconChevronDown } from "../../components/shell/nav-icons";
import type { LibraryFile } from "../../lib/processing/library-mode-api";
import { plural } from "../../lib/ui/mm-plural";
import { CheckCell, NestedFileRow, sourceName } from "./library-file-row";

function groupMeta(rows: LibraryFile[]): string {
  const source = sourceName(rows[0]?.manager_kind);
  const changing = rows.filter(
    (row) => row.classification === "would_change",
  ).length;
  return [
    source ? `${source} · ` : "",
    plural(rows.length, "file", "files"),
    changing > 0 ? ` · ${changing} would change` : "",
  ].join("");
}

/**
 * A title with several files: one row for the title, with the box that selects all of its files, and its files
 * nested under it. The title row folds them away, since a series can run to dozens.
 */
export function LibraryGroup({
  title,
  rows,
  libraryName,
  openPath,
  selected,
  onToggle,
  onOpen,
}: {
  title: string;
  rows: LibraryFile[];
  libraryName: string;
  openPath: string | null;
  selected: Set<string>;
  onToggle: (path: string) => void;
  onOpen: (path: string) => void;
}) {
  const [folded, setFolded] = useState(false);
  const cleanable = rows.filter((row) => row.classification === "would_change");
  const chosen = cleanable.filter((row) => selected.has(row.path)).length;
  const allChosen = cleanable.length > 0 && chosen === cleanable.length;

  // The page toggles one file at a time, so only the files that are not yet where the box is taking them are flipped.
  const toggleAll = () => {
    for (const row of cleanable) {
      if (selected.has(row.path) === allChosen) onToggle(row.path);
    }
  };

  return (
    <div role="rowgroup" className="mm-library-group">
      <div role="row" className="mm-library-row mm-library-row--title">
        <CheckCell
          label={`Select all ${plural(rows.length, "file", "files")} of ${title}`}
          checked={allChosen}
          indeterminate={chosen > 0 && !allChosen}
          disabled={cleanable.length === 0}
          onChange={toggleAll}
        />
        <span
          role="cell"
          className="mm-library-identity mm-library-identity--group"
        >
          <button
            type="button"
            className="mm-library-open"
            aria-expanded={!folded}
            onClick={() => setFolded(!folded)}
          >
            <span className="mm-library-poster">
              <Poster
                url={rows.find((row) => row.poster_url)?.poster_url}
                title={title}
                workflow={libraryName}
              />
            </span>
            <span className="mm-library-lines">
              <span className="mm-library-line mm-library-line--title">
                <span className="mm-library-line__text" title={title}>
                  {title}
                </span>
                <NavIconChevronDown className="mm-library-fold" />
              </span>
              <span className="mm-library-subline">{groupMeta(rows)}</span>
            </span>
          </button>
        </span>
      </div>
      {folded
        ? null
        : rows.map((file) => (
            <NestedFileRow
              key={file.path}
              file={file}
              open={openPath === file.path}
              selected={selected.has(file.path)}
              onToggle={onToggle}
              onOpen={onOpen}
            />
          ))}
    </div>
  );
}
