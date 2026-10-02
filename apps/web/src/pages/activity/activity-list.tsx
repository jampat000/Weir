import { Fragment, type ReactNode } from "react";

import { Chip } from "../../components/panels/chip";
import { FileName } from "../../components/shared/file-name";
import { Poster } from "../../components/shared/poster";
import { SortableColumnHeader } from "../../components/shared/sortable-column-header";
import { formatBytes } from "../../lib/format/bytes";
import {
  processingFileStatusLabel,
  type ProcessingFile,
} from "../../lib/processing/files-api";
import type { LibraryClean } from "../../lib/processing/library-cleans-api";
import type { TableColumns } from "../../lib/ui/use-table-columns";
import type { ActivityColumnId } from "./activity-columns";
import {
  entryMeaning,
  entryPath,
  entryTime,
  type ActivityEntry,
} from "./activity-entries";
import { prettyName } from "../processing/processing-model";
import { agoWords, importedLabel } from "./activity-model";

/** What Weir did, in a few words, for the list. */
function whatWeirDid(file: ProcessingFile): string {
  if (file.status === "processing") {
    return file.progress_percent != null
      ? `Writing · ${Math.round(file.progress_percent)}%`
      : "Working on it";
  }
  return importedLabel(file) ?? processingFileStatusLabel(file);
}

const CLEAN_OUTCOME_WORDS: Record<LibraryClean["outcome"], string> = {
  cleaned: "Cleaned in place",
  skipped: "Already matched the rules",
  failed: "Clean failed",
};

/** The poster of a download; a clean in a library has none. */
function posterUrlOf(entry: ActivityEntry): string | null | undefined {
  return entry.kind === "download" ? entry.file.poster_url : null;
}

function libraryNameOf(entry: ActivityEntry): string {
  return entry.kind === "library_clean"
    ? entry.clean.library_name
    : entry.file.library_name;
}

/** The library name and, for a download, its size. */
function subLine(entry: ActivityEntry): string {
  if (entry.kind === "library_clean") return entry.clean.library_name;
  return [entry.file.library_name, formatBytes(entry.file.size_bytes)]
    .filter(Boolean)
    .join(" · ");
}

function entryIsSelected(entry: ActivityEntry, selectedKey: string | null) {
  return selectedKey !== null && entry.key === selectedKey;
}

function EntryRow({
  entry,
  order,
  selected,
  now,
  onPick,
}: {
  entry: ActivityEntry;
  order: readonly ActivityColumnId[];
  selected: boolean;
  now: number;
  onPick: (entry: ActivityEntry) => void;
}) {
  const cells: Record<ActivityColumnId, ReactNode> = {
    file: (
      <td data-col="file">
        <button
          type="button"
          className="mm-history-file"
          onClick={() => onPick(entry)}
          title={entryPath(entry)}
        >
          <span className="mm-history-file__poster">
            <Poster
              url={posterUrlOf(entry)}
              title={prettyName(entryPath(entry))}
              workflow={libraryNameOf(entry)}
            />
          </span>
          <span className="mm-history-file__text">
            <FileName
              path={entryPath(entry)}
              className="mm-history-file__name"
            />
            <span className="mm-history-file__sub">{subLine(entry)}</span>
          </span>
        </button>
      </td>
    ),
    status: (
      <td data-col="status">
        <Chip meaning={entryMeaning(entry)} className="mm-history-what">
          {entry.kind === "download"
            ? whatWeirDid(entry.file)
            : CLEAN_OUTCOME_WORDS[entry.clean.outcome]}
        </Chip>
      </td>
    ),
    when: (
      <td data-col="when" className="mm-history-when">
        {agoWords(entryTime(entry), now)}
      </td>
    ),
  };
  return (
    <tr
      className={selected ? "is-selected" : undefined}
      aria-current={selected ? "true" : undefined}
    >
      {order.map((id) => (
        <Fragment key={id}>{cells[id]}</Fragment>
      ))}
    </tr>
  );
}

export function ActivityList({
  entries,
  columns,
  selectedKey,
  now,
  onPick,
}: {
  entries: ActivityEntry[];
  columns: TableColumns<ActivityColumnId>;
  selectedKey: string | null;
  now: number;
  onPick: (entry: ActivityEntry) => void;
}) {
  return (
    <table className="mm-history-table" {...columns.tableProps}>
      <thead>
        <tr>
          {columns.order.map((id) => (
            <SortableColumnHeader key={id} heading={columns.heading(id)} />
          ))}
        </tr>
      </thead>
      <tbody>
        {entries.map((entry) => (
          <EntryRow
            key={entry.key}
            entry={entry}
            order={columns.order}
            selected={entryIsSelected(entry, selectedKey)}
            now={now}
            onPick={onPick}
          />
        ))}
      </tbody>
    </table>
  );
}
