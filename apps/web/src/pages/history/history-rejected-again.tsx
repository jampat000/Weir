import { useState } from "react";

import { ConfirmDialog } from "../../components/ui/confirm-dialog";
import { errorMessage } from "../../lib/api/error-message";
import type { RejectedFilesSummary } from "../../lib/processing/rejected-files-api";
import {
  useProcessRejectedFilesAgain,
  useRejectedFilesSummary,
} from "../../lib/processing/rejected-files-queries";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import { plural } from "../../lib/ui/mm-plural";

/**
 * "Process all again": after the rules change, every rejected file whose original is still in its watched folder goes
 * back to work in one step. It covers the whole rejected set (of the workflow History is narrowed to, if any), not just
 * the rows listed for the period on screen, and the dialog says so along with how many files that is.
 */
export function ProcessRejectedAgain({
  libraryId,
  libraryName,
}: {
  libraryId: number | undefined;
  libraryName: string | undefined;
}) {
  const count = useRejectedFilesSummary();
  const processAgain = useProcessRejectedFilesAgain();
  const [notice, setNotice] = useState<string | null>(null);
  const [asking, setAsking] = useState<RejectedFilesSummary | null>(null);
  const scope = libraryName ? `the “${libraryName}” workflow` : "all workflows";

  function ask() {
    setNotice(null);
    count.mutate(libraryId, {
      onSuccess: (summary) => {
        if (summary.ready > 0) {
          processAgain.reset();
          setAsking(summary);
        } else {
          setNotice(nothingToProcess(summary));
        }
      },
      onError: (error) =>
        setNotice(
          errorMessage(error, "Weir could not count the rejected files."),
        ),
    });
  }

  function confirm() {
    processAgain.mutate(libraryId, {
      onSuccess: (result) => {
        setAsking(null);
        setNotice(result.detail);
      },
    });
  }

  return (
    <div className="mm-history-bulk" data-testid="history-process-rejected">
      <button
        type="button"
        className={mmActionButtonClass({ variant: "secondary" })}
        title="Checks every rejected file again with your current rules."
        disabled={count.isPending}
        onClick={ask}
      >
        {count.isPending ? "Checking…" : "Process all again"}
      </button>
      {notice ? (
        <span className="mm-history-note" role="status">
          {notice}
        </span>
      ) : null}
      {asking ? (
        <ConfirmDialog
          testId="history-process-rejected-confirm"
          title={`Process ${plural(asking.ready, "rejected file", "rejected files")} again with your current rules?`}
          description={
            <>
              <p>
                This covers every rejected file in {scope}, not only the ones
                History is listing now. Anything your rules still turn down is
                rejected again.
              </p>
              {asking.rejected > asking.ready ? (
                <p>{skippedSentence(asking.rejected - asking.ready)}</p>
              ) : null}
            </>
          }
          confirmLabel="Process all again"
          cancelLabel="Not now"
          busy={processAgain.isPending}
          busyLabel="Queueing…"
          error={
            processAgain.isError
              ? errorMessage(
                  processAgain.error,
                  "Weir could not queue those files. Try again.",
                )
              : null
          }
          onCancel={() => setAsking(null)}
          onConfirm={confirm}
        />
      ) : null}
    </div>
  );
}

/** What happens to the rejected files whose original is gone. */
function skippedSentence(gone: number): string {
  return `${plural(gone, "other rejected file is", "other rejected files are")} skipped, because ${gone === 1 ? "its original is" : "their originals are"} no longer in the watched folder.`;
}

/** Why the button did nothing: there is nothing rejected, or every original is gone. */
function nothingToProcess(summary: RejectedFilesSummary): string {
  if (summary.rejected === 0) {
    return "There are no rejected files to process again.";
  }
  return summary.rejected === 1
    ? "The one rejected file cannot be processed again: its original is no longer in the watched folder."
    : `None of the ${summary.rejected.toLocaleString()} rejected files can be processed again: their originals are no longer in the watched folder.`;
}
