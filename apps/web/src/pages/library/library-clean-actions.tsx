import { useState } from "react";

import { errorMessage } from "../../lib/api/error-message";
import { formatBytes } from "../../lib/format/bytes";
import { baseName } from "../../lib/format/path";
import type { LibraryCleanResult } from "../../lib/processing/library-mode-api";
import type { useCleanLibraryFiles } from "../../lib/processing/library-mode-queries";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import { plural } from "../../lib/ui/mm-plural";

type CleanMutation = ReturnType<typeof useCleanLibraryFiles>;

/** What a clean did, including the files it would not touch and why. */
function outcomeLine(outcome: LibraryCleanResult): string {
  const queued =
    outcome.queued > 0
      ? `${plural(outcome.queued, "file is", "files are")} queued to clean.`
      : "Nothing was queued.";
  const skipped = outcome.skipped_paths;
  const left =
    skipped.length > 0
      ? ` Weir left ${skipped.length.toLocaleString()} alone: ${skipped
          .map((path) => baseName(path))
          .join(", ")}.`
      : "";
  return queued + left;
}

function Outcome({
  outcome,
  onClose,
}: {
  outcome: LibraryCleanResult;
  onClose: () => void;
}) {
  return (
    <div className="mm-library-outcome" data-testid="library-outcome">
      <p>{outcomeLine(outcome)}</p>
      {outcome.warnings.map((warning) => (
        <p key={warning} className="mm-library-outcome__why">
          {warning}
        </p>
      ))}
      <button type="button" className="mm-head-control" onClick={onClose}>
        Close
      </button>
    </div>
  );
}

/**
 * Cleaning the selected files: the bar that starts it, the server's confirmation when it asks for
 * one, and what the clean did.
 */
export function LibraryCleanActions({
  selected,
  selectedSaving,
  onClearSelection,
  clean,
}: {
  selected: Set<string>;
  /** Bytes the selected files would give back, when the scan could measure it. */
  selectedSaving: number;
  onClearSelection: () => void;
  clean: CleanMutation;
}) {
  const [confirming, setConfirming] = useState<string | null>(null);
  const [outcome, setOutcome] = useState<LibraryCleanResult | null>(null);

  const start = () =>
    clean.mutate(
      { paths: [...selected], confirm: false },
      {
        onSuccess: (result) => {
          if (result.kind === "confirmation_required") {
            setConfirming(result.detail);
            return;
          }
          onClearSelection();
          setOutcome(result);
        },
      },
    );
  const confirm = () =>
    clean.mutate(
      { paths: [...selected], confirm: true },
      {
        onSuccess: (result) => {
          setConfirming(null);
          onClearSelection();
          if (result.kind === "cleaned") setOutcome(result);
        },
      },
    );

  return (
    <>
      {selected.size > 0 ? (
        <div className="mm-library-bulk" data-testid="library-bulk">
          <span>
            <b>{selected.size.toLocaleString()}</b> selected
            {selectedSaving > 0
              ? ` · about ${formatBytes(selectedSaving)} back`
              : ""}
          </span>
          <button
            type="button"
            className={mmActionButtonClass({ variant: "primary" })}
            disabled={clean.isPending}
            onClick={start}
          >
            Clean these files
          </button>
          <button
            type="button"
            className="mm-head-control"
            onClick={onClearSelection}
          >
            Clear
          </button>
        </div>
      ) : null}

      {confirming ? (
        <div
          className="mm-library-confirm"
          role="alertdialog"
          aria-label="Confirm cleaning"
          data-testid="library-confirm"
        >
          <p>{confirming}</p>
          <div className="mm-library-confirm__actions">
            <button
              type="button"
              className={mmActionButtonClass({ variant: "primary" })}
              disabled={clean.isPending}
              onClick={confirm}
            >
              Yes, clean them
            </button>
            <button
              type="button"
              className="mm-head-control"
              onClick={() => setConfirming(null)}
            >
              Not now
            </button>
          </div>
        </div>
      ) : null}

      {outcome ? (
        <Outcome outcome={outcome} onClose={() => setOutcome(null)} />
      ) : null}

      {clean.isError ? (
        <p className="mm-library-error">
          {errorMessage(clean.error, "Weir could not start the clean.")}
        </p>
      ) : null}
    </>
  );
}
