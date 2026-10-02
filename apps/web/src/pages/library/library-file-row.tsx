import { Fragment, type ReactNode } from "react";

import { Poster } from "../../components/shared/poster";
import { formatBytes } from "../../lib/format/bytes";
import { baseName } from "../../lib/format/path";
import { classNames } from "../../lib/ui/class-names";
import type { LibraryFile } from "../../lib/processing/library-mode-api";
import { StatusDot } from "../../components/panels/status-dot";
import type { StatusMeaning } from "../../lib/ui/status-meaning";
import { LIBRARY_CELL_CLASS, type LibraryColumnId } from "./library-columns";
import { fileMeaning, statusNote, statusWords } from "./library-model";

const MANAGER_NAMES: Record<string, string> = {
  sonarr: "Sonarr",
  radarr: "Radarr",
};

/** The media manager a file came from, in the words people use for it. */
export function sourceName(managerKind: string | null | undefined): string {
  if (!managerKind) return "";
  return MANAGER_NAMES[managerKind] ?? managerKind;
}

export function CheckCell({
  label,
  checked,
  indeterminate = false,
  disabled,
  onChange,
}: {
  label: string;
  checked: boolean;
  indeterminate?: boolean;
  disabled: boolean;
  onChange: () => void;
}) {
  return (
    <span role="cell" data-col="select" className="mm-library-select">
      {/* The label is the target: a 24px square around a box drawn at the text's own size. */}
      <label className="mm-library-check-target">
        <input
          type="checkbox"
          className="mm-library-check"
          aria-label={label}
          checked={checked}
          disabled={disabled}
          ref={(box) => {
            if (box) box.indeterminate = indeterminate;
          }}
          onChange={onChange}
        />
      </label>
    </span>
  );
}

/**
 * The "What Weir would do" cell: a file's status in its colour, with a dot, and under it a quiet note: why it needs
 * cleaning, or what Weir did. A title with several files says the same of them all in the same cell.
 */
export function StatusCell({
  meaning,
  words,
  note,
}: {
  meaning: StatusMeaning;
  words: string;
  note?: string | null;
}) {
  return (
    <span
      role="cell"
      data-col="status"
      className="mm-library-verdict"
      title={note ? `${words} (${note})` : words}
    >
      <span className="mm-library-rag mm-status-text" data-status={meaning}>
        <StatusDot meaning={meaning} />
        {words}
      </span>
      {note ? <span className="mm-library-verdict__note">{note}</span> : null}
    </span>
  );
}

/** A cell with nothing to say in its column, which keeps the others under their headings. */
export function EmptyCell({ column }: { column: LibraryColumnId }) {
  return (
    <span
      role="cell"
      data-col={column}
      className={LIBRARY_CELL_CLASS[column]}
    />
  );
}

/** What Weir would do with a file and the figures that go with it, each in the column it belongs to. */
function figureCells(
  file: LibraryFile,
): Record<Exclude<LibraryColumnId, "select" | "title">, ReactNode> {
  const audio = file.audio_summary ?? `${file.audio_track_count}`;
  const subtitles = file.subtitle_summary ?? `${file.subtitle_track_count}`;
  return {
    status: (
      <StatusCell
        meaning={fileMeaning(file)}
        words={statusWords(file)}
        note={statusNote(file)}
      />
    ),
    audio: (
      <span
        role="cell"
        data-col="audio"
        className="mm-library-tracks"
        title={audio}
      >
        {audio}
      </span>
    ),
    subtitles: (
      <span
        role="cell"
        data-col="subtitles"
        className="mm-library-tracks"
        title={subtitles}
      >
        {subtitles}
      </span>
    ),
    size: (
      <span role="cell" data-col="size" className="mm-library-num">
        {formatBytes(file.size_bytes)}
      </span>
    ),
    saved: (
      <span
        role="cell"
        data-col="saved"
        className={classNames(
          "mm-library-num mm-library-back",
          file.estimated_bytes_saved > 0 && "mm-payoff",
        )}
      >
        {file.estimated_bytes_saved > 0
          ? formatBytes(file.estimated_bytes_saved)
          : "—"}
      </span>
    ),
  };
}

/** A row's cells in the table's column order. */
export function OrderedCells({
  order,
  cells,
}: {
  order: readonly LibraryColumnId[];
  cells: Record<LibraryColumnId, ReactNode>;
}) {
  return order.map((id) => <Fragment key={id}>{cells[id]}</Fragment>);
}

type FileRowProps = {
  file: LibraryFile;
  order: readonly LibraryColumnId[];
  open: boolean;
  selected: boolean;
  checkLabel: string;
  onToggle: (path: string) => void;
  onOpen: (path: string) => void;
};

function FileRowFrame({
  file,
  order,
  open,
  selected,
  checkLabel,
  onToggle,
  identity,
}: Omit<FileRowProps, "onOpen"> & { identity: ReactNode }) {
  return (
    <div
      role="row"
      className={`mm-library-row mm-library-row--${file.classification}${
        open ? " mm-library-row--open" : ""
      }`}
      data-testid="library-row"
    >
      <OrderedCells
        order={order}
        cells={{
          select: (
            <CheckCell
              label={checkLabel}
              checked={selected}
              disabled={file.status !== "needs_cleaning"}
              onChange={() => onToggle(file.path)}
            />
          ),
          title: identity,
          ...figureCells(file),
        }}
      />
    </div>
  );
}

/** A title with one file is one row: its poster, its name, and under that the file and where it came from. */
export function SingleFileRow({
  file,
  title,
  libraryName,
  order,
  open,
  selected,
  onToggle,
  onOpen,
}: Omit<FileRowProps, "checkLabel"> & { title: string; libraryName: string }) {
  const source = sourceName(file.manager_kind);
  return (
    <FileRowFrame
      file={file}
      order={order}
      open={open}
      selected={selected}
      checkLabel={`Select ${title}`}
      onToggle={onToggle}
      identity={
        <span role="cell" data-col="title" className="mm-library-identity">
          <button
            type="button"
            className="mm-library-open"
            onClick={() => onOpen(file.path)}
          >
            <span className="mm-library-poster">
              <Poster
                url={file.poster_url}
                title={title}
                workflow={libraryName}
              />
            </span>
            <span className="mm-library-lines">
              <span className="mm-library-line" title={title}>
                {title}
              </span>
              <span className="mm-library-subline">
                <span className="mm-library-subline__name" title={file.path}>
                  {baseName(file.path)}
                </span>
                {source ? (
                  <span className="mm-library-subline__source">
                    <span aria-hidden="true">&middot; </span>
                    {source}
                  </span>
                ) : null}
              </span>
            </span>
          </button>
        </span>
      }
    />
  );
}

/** One of several files under a title: no poster of its own, its name where the title's text starts. */
export function NestedFileRow({
  file,
  order,
  open,
  selected,
  onToggle,
  onOpen,
}: Omit<FileRowProps, "checkLabel">) {
  const name = baseName(file.path);
  return (
    <FileRowFrame
      file={file}
      order={order}
      open={open}
      selected={selected}
      checkLabel={`Select ${name}`}
      onToggle={onToggle}
      identity={
        <span
          role="cell"
          data-col="title"
          className="mm-library-identity mm-library-identity--file"
        >
          <button
            type="button"
            className="mm-library-open"
            onClick={() => onOpen(file.path)}
          >
            <span className="mm-library-line" title={file.path}>
              {name}
            </span>
          </button>
        </span>
      }
    />
  );
}
