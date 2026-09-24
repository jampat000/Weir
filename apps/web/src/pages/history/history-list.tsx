import { FileName } from "../../components/shared/file-name";
import { formatBytes } from "../../lib/format/bytes";
import {
  PROCESSING_FILE_STATUS_LABELS,
  type ProcessingFile,
} from "../../lib/processing/files-api";
import type { LibraryClean } from "../../lib/processing/library-cleans-api";
import {
  entryGroup,
  entryPath,
  entryTime,
  type HistoryEntry,
} from "./history-entries";
import { agoWords, importedLabel } from "./history-model";

/** What Weir did, in a few words, for the list. */
function whatWeirDid(file: ProcessingFile): string {
  if (file.status === "processing") {
    return file.progress_percent != null
      ? `Writing · ${Math.round(file.progress_percent)}%`
      : "Working on it";
  }
  if (file.quarantined) return "Held after repeated failures";
  return (
    importedLabel(file) ??
    PROCESSING_FILE_STATUS_LABELS[file.status] ??
    file.status
  );
}

const CLEAN_OUTCOME_WORDS: Record<LibraryClean["outcome"], string> = {
  cleaned: "Cleaned in place",
  skipped: "Already matched the rules",
  failed: "Clean failed",
};

/** The library name and, for a download, its size. */
function subLine(entry: HistoryEntry): string {
  if (entry.kind === "library_clean") return entry.clean.library_name;
  return [entry.file.library_name, formatBytes(entry.file.size_bytes)]
    .filter(Boolean)
    .join(" · ");
}

function entryIsSelected(entry: HistoryEntry, selectedKey: string | null) {
  return selectedKey !== null && entry.key === selectedKey;
}

export function HistoryList({
  entries,
  selectedKey,
  now,
  onPick,
}: {
  entries: HistoryEntry[];
  selectedKey: string | null;
  now: number;
  onPick: (entry: HistoryEntry) => void;
}) {
  return (
    <table className="mm-history-table">
      <thead>
        <tr>
          <th scope="col">File</th>
          <th scope="col">What happened</th>
          <th scope="col">When</th>
        </tr>
      </thead>
      <tbody>
        {entries.map((entry) => {
          const selected = entryIsSelected(entry, selectedKey);
          return (
            <tr
              key={entry.key}
              className={selected ? "is-selected" : undefined}
              aria-current={selected ? "true" : undefined}
            >
              <td>
                <button
                  type="button"
                  className="mm-history-file"
                  onClick={() => onPick(entry)}
                  title={entryPath(entry)}
                >
                  <FileName
                    path={entryPath(entry)}
                    className="mm-history-file__name"
                  />
                  <span className="mm-history-file__sub">{subLine(entry)}</span>
                </button>
              </td>
              <td>
                <span
                  className={`mm-history-what mm-history-what--${entryGroup(entry) ?? "other"}`}
                >
                  {entry.kind === "download"
                    ? whatWeirDid(entry.file)
                    : CLEAN_OUTCOME_WORDS[entry.clean.outcome]}
                </span>
              </td>
              <td className="mm-history-when">
                {agoWords(entryTime(entry), now)}
              </td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}
