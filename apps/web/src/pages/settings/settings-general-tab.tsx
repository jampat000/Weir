import { useNavigate } from "react-router-dom";
import type { SuiteSettingsOut } from "../../lib/suite/types";
import type {
  useSuiteOperationalHistoryResetMutation,
  useSuiteSettingsSaveMutation,
} from "../../lib/suite/queries";
import {
  QuietDisclosure,
  quietActionRowClass,
} from "../../components/shared/quiet-section";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import { SettingsQuietSection } from "./settings-shared";
import { errorMessage } from "../../lib/api/error-message";

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
  settingsData: SuiteSettingsOut;
};

/** The setup wizard. The time zone that sat above it moved to Settings › Schedule (canvas board 6), where every time
 *  it governs is shown. */
export function SettingsInstanceSection({ settingsData }: InstanceProps) {
  const navigate = useNavigate();
  const wizardState = (settingsData.setup_wizard_state || "pending")
    .trim()
    .toLowerCase();

  return (
    <div data-testid="suite-settings-global" className="mm-quiet-stack">
      {/* Opened once, if ever, so it does not take a heading's worth of the page from the
          things you came here to change. */}
      <QuietDisclosure title="Setup wizard" summaryWhenClosed="Run once">
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
      </QuietDisclosure>
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
        <div className="mm-field-row mt-4">
          <label className="mm-field mm-field--short">
            <span className="mm-field__label">System log retention (days)</span>
            <input
              type="number"
              min={1}
              max={3650}
              className="mm-input"
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
              className="mm-field__hint"
            >
              1 to 3650 days. Older entries are removed while Weir runs.
            </span>
          </label>
          {settingsData.activity_retention_days !== undefined ? (
            <label className="mm-field mm-field--short" id="activity-retention">
              <span className="mm-field__label">
                Keep Activity history for (days)
              </span>
              <input
                type="number"
                min={0}
                max={3650}
                className="mm-input"
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
                className="mm-field__hint"
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
            {errorMessage(save.error, "Could not save.")}
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
              {errorMessage(
                resetHistory.error,
                "Could not reset activity history.",
              )}
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
