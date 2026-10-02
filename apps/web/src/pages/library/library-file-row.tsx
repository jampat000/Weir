import type { ReactNode } from "react";

import { Poster } from "../../components/shared/poster";
import { formatBytes } from "../../lib/format/bytes";
import { baseName } from "../../lib/format/path";
import type { LibraryFile } from "../../lib/processing/library-mode-api";
import { verdictOf } from "./library-model";

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
    <span role="cell" className="mm-library-select">
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

/** What Weir would do with a file and the figures that go with it: the cells every file row ends with. */
function FileFigures({ file }: { file: LibraryFile }) {
  const verdict = verdictOf(file);
  const audio = file.audio_summary ?? `${file.audio_track_count}`;
  const subtitles = file.subtitle_summary ?? `${file.subtitle_track_count}`;
  return (
    <>
      <span role="cell" className="mm-library-verdict" title={verdict}>
        {verdict}
      </span>
      <span role="cell" className="mm-library-tracks" title={audio}>
        {audio}
      </span>
      <span role="cell" className="mm-library-tracks" title={subtitles}>
        {subtitles}
      </span>
      <span role="cell" className="mm-library-num">
        {formatBytes(file.size_bytes)}
      </span>
      <span role="cell" className="mm-library-num mm-library-back">
        {file.estimated_bytes_saved > 0
          ? formatBytes(file.estimated_bytes_saved)
          : "—"}
      </span>
    </>
  );
}

type FileRowProps = {
  file: LibraryFile;
  open: boolean;
  selected: boolean;
  checkLabel: string;
  onToggle: (path: string) => void;
  onOpen: (path: string) => void;
};

function FileRowFrame({
  file,
  open,
  selected,
  checkLabel,
  onToggle,
  children,
}: Omit<FileRowProps, "onOpen"> & { children: ReactNode }) {
  return (
    <div
      role="row"
      className={`mm-library-row mm-library-row--${file.classification}${
        open ? " mm-library-row--open" : ""
      }`}
      data-testid="library-row"
    >
      <CheckCell
        label={checkLabel}
        checked={selected}
        disabled={file.classification !== "would_change"}
        onChange={() => onToggle(file.path)}
      />
      {children}
      <FileFigures file={file} />
    </div>
  );
}

/** A title with one file is one row: its poster, its name, and under that the file and where it came from. */
export function SingleFileRow({
  file,
  title,
  libraryName,
  open,
  selected,
  onToggle,
  onOpen,
}: Omit<FileRowProps, "checkLabel"> & { title: string; libraryName: string }) {
  const source = sourceName(file.manager_kind);
  return (
    <FileRowFrame
      file={file}
      open={open}
      selected={selected}
      checkLabel={`Select ${title}`}
      onToggle={onToggle}
    >
      <span role="cell" className="mm-library-identity">
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
    </FileRowFrame>
  );
}

/** One of several files under a title: no poster of its own, its name where the title's text starts. */
export function NestedFileRow({
  file,
  open,
  selected,
  onToggle,
  onOpen,
}: Omit<FileRowProps, "checkLabel">) {
  const name = baseName(file.path);
  return (
    <FileRowFrame
      file={file}
      open={open}
      selected={selected}
      checkLabel={`Select ${name}`}
      onToggle={onToggle}
    >
      <span
        role="cell"
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
    </FileRowFrame>
  );
}
