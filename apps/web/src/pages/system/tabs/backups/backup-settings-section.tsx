import { useRef } from "react";

import {
  QuietFieldGroup,
  QuietSection,
  quietActionRowClass,
} from "../../../../components/shared/quiet-section";
import { SettingRow } from "../../../../components/shared/settings-group";
import { errorMessage } from "../../../../lib/api/error-message";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { useAppDateFormatter } from "../../../../lib/ui/mm-format-date";
import {
  DEFAULT_BACKUP_TIME,
  type SystemSettingsForm,
} from "../../use-system-settings-form";

const BACKUP_INTERVAL_HOURS = [6, 12, 24, 48, 72, 168] as const;
const HOURS_PER_WEEK = 168;

function ScheduleGroup({
  form,
  lastRunAt,
  onSave,
}: {
  form: SystemSettingsForm;
  lastRunAt: string | null;
  onSave: () => void;
}) {
  const formatDate = useAppDateFormatter();
  const { backupSchedule: schedule, save } = form;
  return (
    <QuietFieldGroup
      title="Scheduled snapshots"
      detail="Weir keeps the latest five configuration snapshots using the same restore-safe JSON format."
    >
      <div className="mm-setgroup__rows">
        <SettingRow
          label="Back up by itself"
          hint="Keeps the latest five, in the same file you can download below."
          htmlFor="backup-scheduled"
        >
          <input
            id="backup-scheduled"
            type="checkbox"
            className="h-4 w-4 accent-mm-accent"
            checked={schedule.enabled}
            disabled={save.isPending}
            onChange={(e) => schedule.setEnabled(e.target.checked)}
          />
        </SettingRow>
        <SettingRow label="Minimum time between runs" htmlFor="backup-interval">
          <select
            id="backup-interval"
            className="mm-input mm-cleanup-every"
            value={schedule.intervalHours}
            disabled={save.isPending}
            onChange={(e) => schedule.setIntervalHours(Number(e.target.value))}
          >
            {BACKUP_INTERVAL_HOURS.map((h) => (
              <option key={h} value={h}>
                {h === HOURS_PER_WEEK ? "Every 7 days" : `Every ${h} hours`}
              </option>
            ))}
          </select>
        </SettingRow>
        <SettingRow label="Preferred backup time" htmlFor="backup-time">
          <input
            id="backup-time"
            type="time"
            className="mm-input mm-cleanup-every"
            value={schedule.time}
            disabled={save.isPending}
            onChange={(e) =>
              schedule.setTime(e.target.value || DEFAULT_BACKUP_TIME)
            }
          />
        </SettingRow>
        <SettingRow label="Last automatic backup">
          <span className="text-sm text-mm-text2">{formatDate(lastRunAt)}</span>
        </SettingRow>
      </div>
      <div className={quietActionRowClass}>
        <button
          type="button"
          className={mmActionButtonClass({ variant: "secondary" })}
          disabled={!schedule.dirty || save.isPending}
          onClick={onSave}
        >
          {save.isPending ? "Saving..." : "Save backup schedule"}
        </button>
        {save.isError && form.lastSaveTarget === "backup" ? (
          <p
            className="mm-status-text--failed text-sm"
            role="alert"
            data-testid="suite-settings-backup-save-error"
          >
            {errorMessage(save.error, "Could not save.")}
          </p>
        ) : null}
      </div>
    </QuietFieldGroup>
  );
}

function ExportGroup({
  disabled,
  onDownload,
  onChooseFile,
}: {
  disabled: boolean;
  onDownload: () => void;
  onChooseFile: (file: File) => void;
}) {
  const fileInput = useRef<HTMLInputElement>(null);
  return (
    <QuietFieldGroup
      title="Export or restore now"
      detail="Download a full settings file, or restore a Weir configuration JSON from disk."
    >
      <div className="flex flex-wrap items-center gap-2">
        <button
          type="button"
          className={mmActionButtonClass({ variant: "secondary" })}
          disabled={disabled}
          onClick={onDownload}
        >
          Download configuration now
        </button>
        <button
          type="button"
          className={mmActionButtonClass({ variant: "tertiary" })}
          disabled={disabled}
          onClick={() => fileInput.current?.click()}
        >
          Restore from file...
        </button>
        <input
          ref={fileInput}
          type="file"
          accept="application/json,.json"
          className="hidden"
          aria-label="Choose configuration JSON file to restore"
          onChange={(e) => {
            const file = e.target.files?.[0];
            // Cleared first, so choosing the same file again still fires a change.
            e.target.value = "";
            if (file) onChooseFile(file);
          }}
        />
      </div>
    </QuietFieldGroup>
  );
}

/**
 * What to back up and when, and a download or restore right now. Two actions with two scopes,
 * each closed by its own row: the group's heading and hairline say which button belongs to which.
 */
export function BackupSettingsSection({
  form,
  lastRunAt,
  busy,
  onSaveSchedule,
  onDownload,
  onChooseFile,
}: {
  form: SystemSettingsForm;
  lastRunAt: string | null;
  busy: boolean;
  onSaveSchedule: () => void;
  onDownload: () => void;
  onChooseFile: (file: File) => void;
}) {
  return (
    <QuietSection
      level={3}
      headingId="suite-settings-backup-heading"
      heading="Backup and restore"
    >
      <p className="mm-quiet-note">
        A backup is Weir&rsquo;s settings: libraries, rules, media managers,
        schedule, alerts and sign-in. Not your media, and not file history.
      </p>
      <div className="mt-6 grid gap-10">
        <ScheduleGroup
          form={form}
          lastRunAt={lastRunAt}
          onSave={onSaveSchedule}
        />
        <ExportGroup
          disabled={busy || form.save.isPending}
          onDownload={onDownload}
          onChooseFile={onChooseFile}
        />
      </div>
    </QuietSection>
  );
}
