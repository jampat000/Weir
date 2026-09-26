import { useState } from "react";

import { FileName } from "../../components/shared/file-name";
import { errorMessage } from "../../lib/api/error-message";
import { formatBytes } from "../../lib/format/bytes";
import type { KeptFile } from "../../lib/processing/kept-files-api";
import { useProcessKeptFileAgain } from "../../lib/processing/kept-files-queries";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";

/** One kept file's row: what it is, where it is, and the one way back. */
function KeptFileRow({
  file,
  editable,
  onProcessed,
}: {
  file: KeptFile;
  editable: boolean;
  onProcessed: (message: string) => void;
}) {
  const processAgain = useProcessKeptFileAgain();
  const [failure, setFailure] = useState<string | null>(null);

  return (
    <tr data-testid={`kept-file-row-${file.id}`}>
      <td>
        <FileName path={file.relative_path} className="mm-history-file__name" />
      </td>
      <td>{file.library_name}</td>
      <td>{formatBytes(file.size_bytes)}</td>
      <td>
        {editable ? (
          <button
            type="button"
            className={mmActionButtonClass({ variant: "secondary" })}
            disabled={processAgain.isPending}
            onClick={() => {
              setFailure(null);
              processAgain.mutate(file.id, {
                onSuccess: (result) => onProcessed(result.detail),
                onError: (error) =>
                  setFailure(
                    errorMessage(
                      error,
                      "That file could not be processed again.",
                    ),
                  ),
              });
            }}
          >
            {processAgain.isPending ? "Queueing…" : "Process again"}
          </button>
        ) : null}
        {failure ? (
          <p className="mm-status-text--failed mt-1 text-sm" role="alert">
            {failure}
          </p>
        ) : null}
      </td>
    </tr>
  );
}

/**
 * The files someone chose to keep without processing again from History's remove dialog (#786 review of #785),
 * listed under History's "Kept" chip since a title someone removed from the rest of the list is where they will
 * look for it. "Process again" clears the marker and asks Weir to look at its library now, exactly as if the file
 * had just appeared.
 */
export function HistoryKeptList({
  files,
  editable,
  onProcessed,
}: {
  files: KeptFile[];
  editable: boolean;
  onProcessed: (message: string) => void;
}) {
  if (files.length === 0) {
    return <p className="mm-history-empty">No files are kept right now.</p>;
  }
  return (
    <table className="mm-history-table" data-testid="kept-files-table">
      <thead>
        <tr>
          <th scope="col">File</th>
          <th scope="col">Library</th>
          <th scope="col">Size</th>
          <th scope="col">
            <span className="sr-only">Actions</span>
          </th>
        </tr>
      </thead>
      <tbody>
        {files.map((file) => (
          <KeptFileRow
            key={file.id}
            file={file}
            editable={editable}
            onProcessed={onProcessed}
          />
        ))}
      </tbody>
    </table>
  );
}
