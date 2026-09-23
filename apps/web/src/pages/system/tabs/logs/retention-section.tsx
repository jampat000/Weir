import {
  QuietSection,
  quietActionRowClass,
} from "../../../../components/shared/quiet-section";
import { errorMessage } from "../../../../lib/api/error-message";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import type { SystemSettingsForm } from "../../use-system-settings-form";

/** How long the System log and Activity history are kept, beside the lists they govern. */
export function RetentionSection({
  form,
  editable,
  savedLogDays,
}: {
  form: SystemSettingsForm;
  editable: boolean;
  savedLogDays: number;
}) {
  const { retention, save } = form;
  const disabled = !editable || save.isPending;

  return (
    <QuietSection
      level={3}
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
            value={retention.logValue}
            disabled={disabled}
            onFocus={() => retention.setLogDraft(String(savedLogDays))}
            onChange={(e) => retention.setLogDraft(e.target.value)}
            onBlur={() =>
              retention.setLogDraft(String(retention.finalizeLogDays()))
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
        <label className="mm-field mm-field--short" id="activity-retention">
          <span className="mm-field__label">
            Keep Activity history for (days)
          </span>
          <input
            type="number"
            min={0}
            max={3650}
            className="mm-input"
            value={retention.activityValue}
            disabled={disabled}
            data-testid="suite-settings-activity-retention"
            onChange={(e) => retention.setActivityDraft(e.target.value)}
            onBlur={() =>
              retention.setActivityDraft(
                String(retention.finalizeActivityDays() ?? ""),
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
      </div>
      {save.isError && form.lastSaveTarget === "logs" ? (
        <p
          className="mt-4 text-sm text-mm-status-failed-text"
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
          disabled={!editable || !retention.dirty || save.isPending}
          data-testid="suite-settings-save-logs"
          onClick={() => form.saveFrom("logs")}
        >
          {save.isPending ? "Saving..." : "Save retention"}
        </button>
      </div>
    </QuietSection>
  );
}
