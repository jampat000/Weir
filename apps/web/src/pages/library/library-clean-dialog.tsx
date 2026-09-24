import { ConfirmDialog } from "../../components/ui/confirm-dialog";
import type {
  LibraryCleanResult,
  LibraryFile,
} from "../../lib/processing/library-mode-api";
import { plural } from "../../lib/ui/mm-plural";
import {
  REMOVAL_IS_FINAL,
  confirmationSummary,
  outcomeLines,
} from "./library-clean-model";
import type { LibraryClean } from "./use-library-clean";

/**
 * The one confirmation every clean passes through, from the selection or from a file's panel. It quotes what
 * the server says the clean removes, and the files Weir's checks will leave out, before anything changes.
 */
export function LibraryCleanConfirm({ flow }: { flow: LibraryClean }) {
  const confirming = flow.confirming;
  if (!confirming) return null;
  const { request, asked } = confirming;
  const count = request.paths.length;
  const warnings = asked.warnings ?? [];

  return (
    <ConfirmDialog
      title={
        request.source === "file"
          ? "Clean this file?"
          : `Clean ${plural(count, "file", "files")}?`
      }
      description={
        <>
          <p>
            {confirmationSummary(asked)} {REMOVAL_IS_FINAL}
          </p>
          {warnings.length > 0 ? (
            <div data-testid="library-confirm-warnings">
              <p>Weir will leave these out of the clean:</p>
              <ul className="mm-library-confirm__warnings">
                {warnings.map((warning) => (
                  <li key={warning}>{warning}</li>
                ))}
              </ul>
            </div>
          ) : null}
        </>
      }
      confirmLabel={count === 1 ? "Yes, clean it" : "Yes, clean them"}
      cancelLabel="Not now"
      busy={flow.confirmPending}
      busyLabel="Starting the clean…"
      error={flow.confirmFailure}
      testId="library-confirm"
      onCancel={flow.cancel}
      onConfirm={flow.confirm}
    />
  );
}

/** What a clean did, including the files it would not touch and why. */
export function LibraryCleanOutcome({
  result,
  known,
  onClose,
}: {
  result: LibraryCleanResult;
  /** The files the page has, which say why a skipped one was skipped. */
  known: readonly LibraryFile[];
  onClose: () => void;
}) {
  return (
    <div
      className="mm-library-outcome"
      role="status"
      data-testid="library-outcome"
    >
      {outcomeLines(result, known).map((line) => (
        <p key={line}>{line}</p>
      ))}
      {result.warnings.map((warning) => (
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
