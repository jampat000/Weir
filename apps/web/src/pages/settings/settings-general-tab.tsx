import { useNavigate } from "react-router-dom";
import type { SuiteSettingsOut } from "../../lib/suite/types";
import type {
  useSuiteOperationalHistoryResetMutation,
  useSuiteSettingsSaveMutation,
} from "../../lib/suite/queries";
import { curatedTimezoneOptionsSorted } from "../../lib/suite/timezone-options";
import { MmListboxPicker } from "../../components/ui/mm-listbox-picker";
import { quietActionRowClass } from "../../components/shared/quiet-section";
import {
  mmActionButtonClass,
  mmEditableTextFieldClass,
} from "../../lib/ui/mm-control-roles";
import { SettingsQuietSection } from "./settings-shared";

/** The field caption, spelled exactly as the deleted `.mm-settings-field-label` rule
 *  spelled it, so losing the cards does not quietly restyle the confirmation field
 *  that guards Clear Activity history. */
const FIELD_LABEL_CLASS =
  "text-[length:var(--mm-type-caption)] font-semibold text-[var(--mm-text2)]";

/** The hint under a field. Same tokens the deleted `.mm-settings-card__hint` used. */
const FIELD_HINT_CLASS =
  "text-[length:var(--mm-type-caption)] leading-[1.45] text-[var(--mm-text3)]";

/**
 * What used to be the General tab, split in two (James, 23 Sep 2026: "too much on each screen").
 * It held six unrelated jobs behind two Save buttons that each wrote something different. Each half
 * now lives with the thing it is about: the time zone and the wizard are facts about this instance,
 * so they sit in System; how long logs and Activity are kept, and clearing that history, sit with
 * History and logs, where you are already looking at what they govern.
 *
 * A form on a page with no cards still has to say where it starts and stops, because each Save
 * writes a different thing. Without a box, that is the section's own hairline above it and the
 * `.mm-quiet-stack`'s 2.5rem below it: one column of full-width groups, each closed by its own
 * action row.
 */
type InstanceProps = {
  editable: boolean;
  settingsData: SuiteSettingsOut;
  save: ReturnType<typeof useSuiteSettingsSaveMutation>;
  appTimezone: string | null;
  setAppTimezone: (v: string) => void;
  timezoneDirty: boolean;
  lastSuiteSaveTarget: "timezone" | "logs" | "backup" | null;
  onSaveTimezone: () => void;
};

export function SettingsInstanceSection({
  editable,
  settingsData,
  save,
  appTimezone,
  setAppTimezone,
  timezoneDirty,
  lastSuiteSaveTarget,
  onSaveTimezone,
}: InstanceProps) {
  const navigate = useNavigate();
  const timezoneOptions = curatedTimezoneOptionsSorted();
  const wizardState = (settingsData.setup_wizard_state || "pending")
    .trim()
    .toLowerCase();

  return (
    <div data-testid="suite-settings-global" className="mm-quiet-stack">
      {!editable ? (
        <p className="mm-quiet-note">
          Operators and admins can change these; everyone can read History and
          logs.
        </p>
      ) : null}

      <SettingsQuietSection
        headingId="suite-settings-timezone-heading"
        heading="Time zone"
      >
        <p className="mm-quiet-note">
          Times across Weir, and schedule windows, use this zone.
        </p>
        <div className="mt-4 max-w-md min-w-0">
          <MmListboxPicker
            ariaLabelledBy="suite-settings-timezone-heading"
            ariaDescribedBy="suite-timezone-hint"
            placeholder="Select time zone"
            disabled={!editable || save.isPending}
            options={timezoneOptions.map((tz) => ({
              value: tz.id,
              label: tz.label,
            }))}
            value={appTimezone ?? ""}
            onChange={(v) => setAppTimezone(v)}
          />
          <p
            id="suite-timezone-hint"
            className={`mt-2 block ${FIELD_HINT_CLASS}`}
          >
            Not listed? Pick a city in the same zone; it only changes how times
            are shown.
          </p>
        </div>
        {save.isError && lastSuiteSaveTarget === "timezone" ? (
          <p
            className="mt-4 text-sm text-[var(--mm-status-failed-text)]"
            role="alert"
            data-testid="suite-settings-timezone-save-error"
          >
            {save.error instanceof Error
              ? save.error.message
              : "Could not save."}
          </p>
        ) : null}
        <div className={`${quietActionRowClass} mt-6`}>
          <button
            type="button"
            className={mmActionButtonClass({ variant: "primary" })}
            disabled={!editable || !timezoneDirty || save.isPending}
            data-testid="suite-settings-save-timezone"
            onClick={() => onSaveTimezone()}
          >
            {save.isPending ? "Saving..." : "Save time zone"}
          </button>
        </div>
      </SettingsQuietSection>

      <SettingsQuietSection
        headingId="suite-settings-wizard-heading"
        heading="Setup wizard"
      >
        <p className="mm-quiet-note">
          Go through the first-run steps again: time zone, backups and library
          folders. You can leave at any point.
        </p>
        <p className={`mt-3 block ${FIELD_HINT_CLASS}`}>
          {wizardState === "completed"
            ? "You finished the wizard."
            : wizardState === "skipped"
              ? "You skipped the wizard."
              : "The wizard has not been finished yet."}
        </p>
        <div className={`${quietActionRowClass} mt-6`}>
          <button
            type="button"
            className={mmActionButtonClass({ variant: "secondary" })}
            data-testid="suite-settings-open-setup-wizard"
            onClick={() => navigate("/setup-wizard")}
          >
            Open setup wizard
          </button>
        </div>
      </SettingsQuietSection>
    </div>
  );
}

type RetentionProps = {
  editable: boolean;
  settingsData: SuiteSettingsOut;
  save: ReturnType<typeof useSuiteSettingsSaveMutation>;
  setLogRetentionDaysDraft: (v: string | null) => void;
  normalizedLogRetentionDraft: string;
  finalizeLogRetentionDays: () => number;
  logsDirty: boolean;
  normalizedActivityRetentionDraft: string;
  setActivityRetentionDaysDraft: (v: string | null) => void;
  finalizeActivityRetentionDays: () => number | undefined;
  lastSuiteSaveTarget: "timezone" | "logs" | "backup" | null;
  resetHistoryConfirm: string;
  setResetHistoryConfirm: (v: string) => void;
  resetHistory: ReturnType<typeof useSuiteOperationalHistoryResetMutation>;
  resetHistoryMsg: string | null;
  onSaveLogs: () => void;
  onResetOperationalHistory: () => void;
};

/** How long what you are looking at is kept, and how to empty it. Shown under History and logs. */
export function SettingsHistoryRetentionSection({
  editable,
  settingsData,
  save,
  setLogRetentionDaysDraft,
  normalizedLogRetentionDraft,
  finalizeLogRetentionDays,
  logsDirty,
  normalizedActivityRetentionDraft,
  setActivityRetentionDaysDraft,
  finalizeActivityRetentionDays,
  lastSuiteSaveTarget,
  resetHistoryConfirm,
  setResetHistoryConfirm,
  resetHistory,
  resetHistoryMsg,
  onSaveLogs,
  onResetOperationalHistory,
}: RetentionProps) {
  return (
    <div data-testid="suite-settings-retention" className="mm-quiet-stack">
      <SettingsQuietSection
        headingId="suite-settings-log-retention-heading"
        heading="How long this is kept"
      >
        <p className="mm-quiet-note">
          How long Weir keeps its system log and how far back Activity goes.
        </p>
        <div className="mt-4 grid max-w-2xl gap-5 sm:grid-cols-2">
          <label className="block min-w-0">
            <span className={FIELD_LABEL_CLASS}>
              System log retention (days)
            </span>
            <input
              type="number"
              min={1}
              max={3650}
              className={`${mmEditableTextFieldClass} mt-1`}
              value={normalizedLogRetentionDraft}
              disabled={!editable || save.isPending}
              onFocus={() =>
                setLogRetentionDaysDraft(
                  String(settingsData.log_retention_days),
                )
              }
              onChange={(e) => setLogRetentionDaysDraft(e.target.value)}
              onBlur={() =>
                setLogRetentionDaysDraft(String(finalizeLogRetentionDays()))
              }
              aria-describedby="suite-general-log-retention-hint"
            />
            <span
              id="suite-general-log-retention-hint"
              className={`mt-1 block ${FIELD_HINT_CLASS}`}
            >
              1 to 3650 days. Older entries are removed while Weir runs.
            </span>
          </label>
          {settingsData.activity_retention_days !== undefined ? (
            <label className="block min-w-0" id="activity-retention">
              <span className={FIELD_LABEL_CLASS}>
                Keep Activity history for (days)
              </span>
              <input
                type="number"
                min={0}
                max={3650}
                className={`${mmEditableTextFieldClass} mt-1`}
                value={normalizedActivityRetentionDraft}
                disabled={!editable || save.isPending}
                data-testid="suite-settings-activity-retention"
                onChange={(e) => setActivityRetentionDaysDraft(e.target.value)}
                onBlur={() =>
                  setActivityRetentionDaysDraft(
                    String(finalizeActivityRetentionDays() ?? ""),
                  )
                }
                aria-describedby="suite-general-activity-retention-hint"
              />
              <span
                id="suite-general-activity-retention-hint"
                className={`mt-1 block ${FIELD_HINT_CLASS}`}
              >
                0 keeps it until you clear it. Media files are never touched.
              </span>
            </label>
          ) : null}
        </div>
        {save.isError && lastSuiteSaveTarget === "logs" ? (
          <p
            className="mt-4 text-sm text-[var(--mm-status-failed-text)]"
            role="alert"
            data-testid="suite-settings-logs-save-error"
          >
            {save.error instanceof Error
              ? save.error.message
              : "Could not save."}
          </p>
        ) : null}
        <div className={`${quietActionRowClass} mt-6`}>
          <button
            type="button"
            className={mmActionButtonClass({ variant: "primary" })}
            disabled={!editable || !logsDirty || save.isPending}
            data-testid="suite-settings-save-logs"
            onClick={() => onSaveLogs()}
          >
            {save.isPending ? "Saving..." : "Save retention"}
          </button>
        </div>
      </SettingsQuietSection>

      {editable ? (
        <SettingsQuietSection
          headingId="suite-settings-history-reset-heading"
          heading="Clear Activity history"
          id="history-reset"
          data-testid="suite-settings-history-reset"
        >
          <p className="mm-quiet-note">
            Removes every Activity entry and finished job record now. Media
            files are not touched. Signing out does not clear history.
          </p>
          <label className="mt-4 block max-w-md min-w-0">
            <span className={FIELD_LABEL_CLASS}>Type RESET to confirm</span>
            <input
              type="text"
              className="mm-input mt-1 w-full"
              value={resetHistoryConfirm}
              disabled={resetHistory.isPending}
              autoComplete="off"
              onChange={(e) => setResetHistoryConfirm(e.target.value)}
            />
          </label>
          {resetHistoryMsg ? (
            <p
              className="mt-4 max-w-md rounded-md border border-[var(--mm-border)] bg-[var(--mm-status-healthy-bg)] px-3 py-2 text-sm text-[var(--mm-status-healthy-text)]"
              role="status"
            >
              {resetHistoryMsg}
            </p>
          ) : null}
          {resetHistory.isError ? (
            <p
              className="mt-4 max-w-md rounded-md border border-[var(--mm-border)] bg-[var(--mm-status-failed-bg)] px-3 py-2 text-sm text-[var(--mm-status-failed-text)]"
              role="alert"
            >
              {resetHistory.error instanceof Error
                ? resetHistory.error.message
                : "Could not reset activity history."}
            </p>
          ) : null}
          <div className={`${quietActionRowClass} mt-6`}>
            <button
              type="button"
              className={mmActionButtonClass({ variant: "tertiary" })}
              disabled={
                resetHistory.isPending ||
                resetHistoryConfirm.trim().toUpperCase() !== "RESET"
              }
              onClick={() => onResetOperationalHistory()}
            >
              {resetHistory.isPending ? "Resetting..." : "Reset history"}
            </button>
          </div>
        </SettingsQuietSection>
      ) : null}
    </div>
  );
}
