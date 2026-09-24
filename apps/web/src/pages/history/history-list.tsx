import { FileName } from "../../components/shared/file-name";
import { formatBytes } from "../../lib/format/bytes";
import {
  PROCESSING_FILE_STATUS_LABELS,
  type ProcessingFile,
} from "../../lib/processing/files-api";
import { agoWords, historyGroupOf, importedLabel } from "./history-model";

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

export function HistoryList({
  files,
  selectedId,
  now,
  onPick,
}: {
  files: ProcessingFile[];
  selectedId: number | null;
  now: number;
  onPick: (id: number) => void;
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
        {files.map((file) => (
          <tr
            key={file.id}
            className={file.id === selectedId ? "is-selected" : undefined}
            aria-selected={file.id === selectedId}
          >
            <td>
              <button
                type="button"
                className="mm-history-file"
                onClick={() => onPick(file.id)}
                title={file.relative_path}
              >
                <FileName
                  path={file.relative_path}
                  className="mm-history-file__name"
                />
                <span className="mm-history-file__sub">
                  {[file.library_name, formatBytes(file.size_bytes)]
                    .filter(Boolean)
                    .join(" · ")}
                </span>
              </button>
            </td>
            <td>
              <span
                className={`mm-history-what mm-history-what--${historyGroupOf(file) ?? "other"}`}
              >
                {whatWeirDid(file)}
              </span>
            </td>
            <td className="mm-history-when">
              {agoWords(file.updated_at, now)}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
