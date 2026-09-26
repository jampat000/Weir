import { useId, useState } from "react";

import { FileName } from "../../../../components/shared/file-name";
import { PageLoading } from "../../../../components/shared/page-loading";
import { QuietSection } from "../../../../components/shared/quiet-section";
import { errorMessage } from "../../../../lib/api/error-message";
import { formatBytes } from "../../../../lib/format/bytes";
import type { KeptFile } from "../../../../lib/processing/kept-files-api";
import {
  useKeptFilesQuery,
  useProcessKeptFileAgain,
} from "../../../../lib/processing/kept-files-queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { SettingsLoadError } from "../../settings-load-error";

/** One kept file's row: what it is, where it is, and the one way back. */
function KeptFileRow({ file }: { file: KeptFile }) {
  const processAgain = useProcessKeptFileAgain();
  const [notice, setNotice] = useState<string | null>(null);
  const [failure, setFailure] = useState<string | null>(null);

  return (
    <tr data-testid={`kept-file-row-${file.id}`}>
      <td>
        <FileName path={file.relative_path} />
      </td>
      <td>{file.library_name}</td>
      <td>{formatBytes(file.size_bytes)}</td>
      <td>
        <button
          type="button"
          className={mmActionButtonClass({ variant: "secondary" })}
          disabled={processAgain.isPending}
          onClick={() => {
            setNotice(null);
            setFailure(null);
            processAgain.mutate(file.id, {
              onSuccess: (result) => setNotice(result.detail),
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
        {notice ? (
          <p className="mm-quiet-note mt-1" role="status">
            {notice}
          </p>
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
 * Every file someone chose to keep without processing again from History's remove dialog (#785), and the one way
 * back: "Process again" clears the marker and asks Weir to look at its library now, exactly as if the file had
 * just appeared.
 */
export function KeptFilesSection() {
  const headingId = useId();
  const q = useKeptFilesQuery();

  return (
    <QuietSection headingId={headingId} heading="Kept files" level={3}>
      <p className="mm-quiet-note">
        Files you chose to keep without processing again. Weir leaves each one
        alone until it changes.
      </p>
      {q.isPending ? (
        <PageLoading label="Loading kept files" />
      ) : q.isError ? (
        <SettingsLoadError what="kept files" />
      ) : q.data.files.length === 0 ? (
        <p className="mm-quiet-note mt-3">No files are kept right now.</p>
      ) : (
        <div className="mm-quiet-table-wrap mt-4">
          <table className="mm-quiet-table" data-testid="kept-files-table">
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
              {q.data.files.map((file) => (
                <KeptFileRow key={file.id} file={file} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </QuietSection>
  );
}
