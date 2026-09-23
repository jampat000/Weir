import { useState } from "react";

import {
  QuietSection,
  quietActionRowClass,
} from "../../../../components/shared/quiet-section";
import { errorMessage } from "../../../../lib/api/error-message";
import { useHistoryResetMutation } from "../../../../lib/settings/queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { plural } from "../../../../lib/ui/mm-plural";

/** The word that must be typed before history is cleared. */
const CONFIRM_WORD = "RESET";

/** Clearing Activity history cannot be undone, so it asks for RESET to be typed first. */
export function HistoryResetSection() {
  const resetHistory = useHistoryResetMutation();
  const [typed, setTyped] = useState("");
  const [result, setResult] = useState<string | null>(null);

  const reset = () => {
    setResult(null);
    resetHistory.mutate(typed, {
      onSuccess: (out) => {
        setTyped("");
        setResult(
          `History reset. Removed ${plural(out.total_deleted, "old history item", "old history items")}.`,
        );
      },
    });
  };

  return (
    <QuietSection
      level={3}
      headingId="suite-settings-history-reset-heading"
      heading="Clear Activity history"
      id="history-reset"
      data-testid="suite-settings-history-reset"
    >
      <p className="mm-quiet-note">
        Removes every Activity entry and finished job record now. Media files
        are not touched. Signing out does not clear history.
      </p>
      <label className="mt-4 block max-w-md min-w-0">
        <span className="mm-history-reset__label">Type RESET to confirm</span>
        <input
          type="text"
          className="mm-input mt-1 w-full"
          value={typed}
          disabled={resetHistory.isPending}
          autoComplete="off"
          onChange={(e) => setTyped(e.target.value)}
        />
      </label>
      {result ? (
        <p
          className="mm-history-reset__outcome mm-history-reset__outcome--done"
          role="status"
        >
          {result}
        </p>
      ) : null}
      {resetHistory.isError ? (
        <p
          className="mm-history-reset__outcome mm-history-reset__outcome--failed"
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
            typed.trim().toUpperCase() !== CONFIRM_WORD
          }
          onClick={reset}
        >
          {resetHistory.isPending ? "Resetting..." : "Reset history"}
        </button>
      </div>
    </QuietSection>
  );
}
