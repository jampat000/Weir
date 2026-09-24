import { formatBytes } from "../../lib/format/bytes";
import type { LibraryFile } from "../../lib/processing/library-mode-api";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import { LibraryCleanOutcome } from "./library-clean-dialog";
import type { LibraryClean } from "./use-library-clean";

/** Cleaning the selected files: the bar that starts it, why it could not start, and what it did. */
export function LibraryCleanActions({
  selected,
  selectedSaving,
  known,
  onClearSelection,
  flow,
}: {
  selected: Set<string>;
  /** Bytes the selected files would give back, when the scan could measure it. */
  selectedSaving: number;
  known: readonly LibraryFile[];
  onClearSelection: () => void;
  flow: LibraryClean;
}) {
  const pending = flow.isPending("selection");
  const failure = flow.failure("selection");
  const outcome =
    flow.outcome?.request.source === "selection" ? flow.outcome : null;

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
            disabled={pending}
            onClick={() =>
              flow.start({ source: "selection", paths: [...selected] })
            }
          >
            {pending ? "Checking these files…" : "Clean these files"}
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

      {outcome ? (
        <LibraryCleanOutcome
          result={outcome.result}
          known={known}
          onClose={flow.dismissOutcome}
        />
      ) : null}

      {failure ? (
        <p className="mm-library-error" role="alert">
          {failure}
        </p>
      ) : null}
    </>
  );
}
