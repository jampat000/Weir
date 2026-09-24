import { useQueryClient } from "@tanstack/react-query";

import { ConfirmDialog } from "../../../../components/ui/confirm-dialog";
import { errorMessage } from "../../../../lib/api/error-message";
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

  const saveSchedule = () => {
    actions.setProblem(null);
    actions.setMessage(null);
    form.saveFrom("backup", {
      onSaved: () => {
        actions.setMessage("Backup schedule saved.");
        void queryClient.invalidateQueries({
          queryKey: settingsKeys.configurationBackups,
        });
      },
      onFailed: (error) =>
        actions.setProblem(
          errorMessage(error, "Could not save backup schedule."),
        ),
    });
  };

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
              onDownload={() => void actions.downloadConfiguration()}
              onChooseFile={(file) => void actions.chooseRestoreFile(file)}
            />
            <BackupListSection
              backupsQ={backupsQ}
              disabled={blocked}
              onDownload={(id, fileName) =>
                void actions.downloadSnapshot(id, fileName)
              }
            />
            {actions.message ? (
              <p
                className="mm-status-text--healthy mm-backups-grid__wide text-sm"
                role="status"
              >
                {actions.message}
              </p>
            ) : null}
            {actions.problem ? (
              <p
                className="mm-status-text--failed mm-backups-grid__wide text-sm"
                role="alert"
              >
                {actions.problem}
              </p>
            ) : null}
          </div>
        ) : null}
      </div>
      {actions.pendingRestore ? (
        <ConfirmDialog
          title="Replace the settings on this server from this file?"
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
