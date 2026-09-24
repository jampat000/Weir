import { useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { ConfirmDialog } from "../../../../components/ui/confirm-dialog";
import { useConfigurationBackupsQuery } from "../../../../lib/settings/queries";
import { settingsKeys } from "../../../../lib/settings/query-keys";
import type { AppSettings } from "../../../../lib/settings/types";
import type { SystemSettingsForm } from "../../use-system-settings-form";
import { BackupListSection } from "./backup-list-section";
import { BackupSettingsSection } from "./backup-settings-section";
import { useBackupActions } from "./use-backup-actions";

/** System › Backups: what to back up and when on the left, the backups on this machine on the right. */
export function BackupsTab({
  form,
  editable,
  settings,
}: {
  form: SystemSettingsForm;
  editable: boolean;
  settings: AppSettings;
}) {
  const queryClient = useQueryClient();
  const backupsQ = useConfigurationBackupsQuery(editable);
  const actions = useBackupActions();
  const blocked = actions.busy || form.save.isPending;
  // The schedule's own failure already shows beside its Save button (form.save.isError); this is
  // only the success line, so it can sit right there too instead of in a shared banner.
  const [scheduleSaved, setScheduleSaved] = useState(false);

  const saveSchedule = () => {
    setScheduleSaved(false);
    form.saveFrom("backup", {
      onSaved: () => {
        setScheduleSaved(true);
        void queryClient.invalidateQueries({
          queryKey: settingsKeys.configurationBackups,
        });
      },
      onFailed: () => setScheduleSaved(false),
    });
  };

  const exportResult = actions.resultFor("export");
  const snapshotResult = actions.resultFor("snapshot");

  return (
    <div className="mm-quiet-stack">
      <div data-testid="suite-settings-backup-tab" className="mm-quiet-stack">
        {editable ? (
          <div
            className="mm-backups-grid"
            data-testid="suite-settings-backup-restore"
          >
            <BackupSettingsSection
              form={form}
              lastRunAt={settings.configuration_backup_last_run_at}
              busy={actions.busy}
              onSaveSchedule={saveSchedule}
              scheduleSaved={scheduleSaved}
              onBackUpNow={() => void actions.backUpNow()}
              onDownload={() => void actions.downloadConfiguration()}
              onChooseFile={(file) => void actions.chooseRestoreFile(file)}
              resultMessage={exportResult.message}
              resultProblem={exportResult.problem}
            />
            <BackupListSection
              backupsQ={backupsQ}
              disabled={blocked}
              onDownload={(id, fileName) =>
                void actions.downloadSnapshot(id, fileName)
              }
              onRestore={(id) => void actions.chooseSavedBackup(id)}
              resultMessage={snapshotResult.message}
              resultProblem={snapshotResult.problem}
            />
          </div>
        ) : null}
      </div>
      {actions.pendingRestore ? (
        <ConfirmDialog
          title="Replace the settings on this server with this backup?"
          description="This cannot be undone."
          confirmLabel="Replace settings"
          cancelLabel="Keep current settings"
          testId="restore-configuration-dialog"
          onCancel={actions.cancelRestore}
          onConfirm={actions.confirmRestore}
        />
      ) : null}
    </div>
  );
}
