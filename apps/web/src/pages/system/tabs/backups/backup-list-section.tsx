import { QuietSection } from "../../../../components/shared/quiet-section";
import { errorMessage } from "../../../../lib/api/error-message";
import { formatBytes } from "../../../../lib/format/bytes";
import type { useConfigurationBackupsQuery } from "../../../../lib/settings/queries";
import { useAppDateFormatter } from "../../../../lib/ui/mm-format-date";

/** The snapshots on this machine, newest first, each one a download. */
export function BackupListSection({
  backupsQ,
  disabled,
  onDownload,
}: {
  backupsQ: ReturnType<typeof useConfigurationBackupsQuery>;
  disabled: boolean;
  onDownload: (id: number, fileName: string) => void;
}) {
  const formatDate = useAppDateFormatter();
  const items = backupsQ.data?.items ?? [];

  return (
    <QuietSection
      level={3}
      headingId="suite-settings-backup-snapshots-heading"
      heading="Backups on this machine"
      aside={<span className="mm-quiet-badge">Keeps latest 5</span>}
    >
      {backupsQ.data ? (
        <p
          className="mm-quiet-table__sub font-mono break-all"
          data-testid="suite-configuration-backup-directory"
        >
          {backupsQ.data.directory}
        </p>
      ) : null}
      <div className="mt-3">
        {backupsQ.isLoading ? (
          <p className="mm-quiet-note">Loading snapshot list...</p>
        ) : backupsQ.isError ? (
          <p className="text-sm text-mm-status-failed-text" role="alert">
            {errorMessage(backupsQ.error, "Could not load backups.")}
          </p>
        ) : items.length === 0 ? (
          <p className="mm-quiet-note">No automatic snapshots yet.</p>
        ) : (
          <div className="mm-quiet-table-wrap">
            <table className="mm-quiet-table">
              <thead>
                <tr>
                  <th scope="col">Taken</th>
                  <th scope="col">Size</th>
                  <th scope="col">
                    <span className="sr-only">Download</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {items.map((row) => (
                  <tr key={row.id}>
                    <th scope="row" className="mm-quiet-table__name">
                      {formatDate(row.created_at)}
                    </th>
                    <td data-label="Size">{formatBytes(row.size_bytes)}</td>
                    <td data-label="">
                      <button
                        type="button"
                        className="mm-quiet-link"
                        disabled={disabled}
                        onClick={() => onDownload(row.id, row.file_name)}
                      >
                        Download snapshot →
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </QuietSection>
  );
}
