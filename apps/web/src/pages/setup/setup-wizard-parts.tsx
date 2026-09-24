import type { ReactNode } from "react";
import { Link } from "react-router-dom";

import { AuthBrandStack } from "../../components/brand/auth-brand-stack";
import { ServerFolderPickerButton } from "../../components/ui/server-folder-picker-button";

const BACKUP_INTERVAL_OPTIONS = [
  { value: "24", label: "Every day" },
  { value: "48", label: "Every 2 days" },
  { value: "168", label: "Every week" },
] as const;

export const DEFAULT_BACKUP_TIME = "02:00";

export function WizardSection({
  headingId,
  title,
  description,
  children,
}: {
  headingId: string;
  title: string;
  description: string;
  children: ReactNode;
}) {
  return (
    <section className="mm-quiet-section" aria-labelledby={headingId}>
      <div className="mm-quiet-section__head">
        <h2 id={headingId} className="mm-quiet-section__title">
          {title}
        </h2>
      </div>
      <div className="mm-quiet-section__body">
        <div className="flex flex-col gap-3">
          <p className="mm-quiet-note">{description}</p>
          {children}
        </div>
      </div>
    </section>
  );
}

export function FolderInput({
  id,
  label,
  visibleLabel,
  hint,
  value,
  disabled,
  onChange,
}: {
  id: string;
  /** Full name for assistive tech, e.g. "Movies watched folder". */
  label: string;
  visibleLabel: string;
  hint: string;
  value: string;
  disabled: boolean;
  onChange: (value: string) => void;
}) {
  return (
    <div className="min-w-0">
      <label htmlFor={id} className="mm-wizard-label">
        {visibleLabel}
      </label>
      <div className="mm-wizard-folder">
        <input
          id={id}
          className="mm-input w-full"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={hint}
          aria-label={label}
          disabled={disabled}
        />
        <ServerFolderPickerButton
          title={`Choose ${label}`}
          value={value}
          disabled={disabled}
          onSelect={onChange}
        />
      </div>
    </div>
  );
}

export type BackupDraft = {
  enabled: boolean;
  intervalHours: string;
  preferredTime: string;
};

export function BackupFields({
  draft,
  onChange,
}: {
  draft: BackupDraft;
  onChange: (draft: BackupDraft) => void;
}) {
  return (
    <>
      <label className="flex cursor-pointer items-start gap-2.5 text-sm text-mm-text1">
        <input
          type="checkbox"
          className="mt-0.5 h-4 w-4 shrink-0 accent-mm-accent"
          checked={draft.enabled}
          onChange={(e) => onChange({ ...draft, enabled: e.target.checked })}
        />
        <span>Back up the configuration automatically</span>
      </label>
      <div className="mm-wizard-backup-fields">
        <label className="block min-w-0">
          <span className="mm-wizard-label">Minimum time between backups</span>
          <select
            className="mm-input w-full"
            value={draft.intervalHours}
            disabled={!draft.enabled}
            onChange={(e) =>
              onChange({ ...draft, intervalHours: e.target.value })
            }
          >
            {BACKUP_INTERVAL_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </label>
        <label className="block min-w-0">
          <span className="mm-wizard-label">Preferred time</span>
          <input
            type="time"
            className="mm-input w-full"
            value={draft.preferredTime}
            disabled={!draft.enabled}
            onChange={(e) =>
              onChange({
                ...draft,
                preferredTime: e.target.value || DEFAULT_BACKUP_TIME,
              })
            }
          />
        </label>
      </div>
    </>
  );
}

export function WizardLoadFailed() {
  return (
    <main className="mm-auth-body" id="mm-main-content" tabIndex={-1}>
      <div className="mm-auth-frame">
        <AuthBrandStack />
        <div className="mm-auth-card">
          <p className="mm-auth-eyebrow">Setup wizard</p>
          <h1 className="mm-auth-title">Could not load setup</h1>
          <p className="mm-auth-lead">
            The wizard could not load the current settings. Open Settings later
            and try again.
          </p>
          <p className="mm-auth-footer-link">
            <Link to="/">Continue to the app</Link>
          </p>
        </div>
      </div>
    </main>
  );
}
