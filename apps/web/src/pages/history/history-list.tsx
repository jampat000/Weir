import { Chip } from "../../components/panels/chip";
import type { MmStatusTone } from "../../lib/ui/mm-status-tone";
import { FileName } from "../../components/shared/file-name";
import { Poster } from "../../components/shared/poster";
import { formatBytes } from "../../lib/format/bytes";
import {
  processingFileStatusLabel,
  type ProcessingFile,
} from "../../lib/processing/files-api";
import type { LibraryClean } from "../../lib/processing/library-cleans-api";
import {
  entryGroup,
  type HistoryGroup,
  entryPath,
  entryTime,
  type HistoryEntry,
} from "./history-entries";
import { prettyName } from "../processing/processing-model";
import { agoWords, importedLabel } from "./history-model";

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
function posterUrlOf(entry: HistoryEntry): string | null | undefined {
  return entry.kind === "download" ? entry.file.poster_url : null;
}

function libraryNameOf(entry: HistoryEntry): string {
  return entry.kind === "library_clean"
    ? entry.clean.library_name
    : entry.file.library_name;
}

/** The library name and, for a download, its size. */
function subLine(entry: HistoryEntry): string {
  if (entry.kind === "library_clean") return entry.clean.library_name;
  return [entry.file.library_name, formatBytes(entry.file.size_bytes)]
    .filter(Boolean)
    .join(" · ");
}

/** The chip's colour for where an entry stands. A skip is Weir deciding a file is not for it, so it stays neutral. */
const GROUP_TONE: Record<HistoryGroup, MmStatusTone> = {
  all: "neutral",
  working: "info",
  finished: "healthy",
  attention: "warning",
  needs: "warning",
  skipped: "neutral",
  failed: "failed",
  kept: "neutral",
};

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
                    <span className="mm-history-file__sub">
                      {subLine(entry)}
                    </span>
                  </span>
                </button>
              </td>
              <td>
                <Chip
                  tone={GROUP_TONE[entryGroup(entry) ?? "all"]}
                  className="mm-history-what"
                >
                  {entry.kind === "download"
                    ? whatWeirDid(entry.file)
                    : CLEAN_OUTCOME_WORDS[entry.clean.outcome]}
                </Chip>
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
