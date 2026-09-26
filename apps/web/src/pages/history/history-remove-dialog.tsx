import { useState } from "react";

import { ConfirmDialog } from "../../components/ui/confirm-dialog";
import type {
  ProcessingFileRemoveOptions,
  ProcessingFileRemovalResolution,
} from "../../lib/processing/files-api";

/** The three choices offered when a title's file is still in the watched folder (#785). */
type HistoryRemoveChoice = Extract<
  ProcessingFileRemovalResolution,
  "delete" | "keep" | "retry"
>;

/** The label and explanation for one of the three choices, built from what `remove-options` reported. */
function choices(
  options: ProcessingFileRemoveOptions,
): { value: HistoryRemoveChoice; label: string; detail: string }[] {
  const manager = options.manager_label;
  return [
    {
      value: "delete",
      label:
        options.delete_handled_by_manager && manager
          ? `Delete the download and ask ${manager} for another copy`
          : "Delete the file",
      detail:
        options.delete_handled_by_manager && manager
          ? `${manager} removes it, blocks this release and searches again.`
          : "Weir alone: the file is deleted.",
    },
    {
      value: "keep",
      label: "Keep the file, but don't process it again",
      detail:
        options.keep_notifies_manager && manager
          ? `Weir leaves it until it changes. ${manager} is told it won't be imported.`
          : "Weir leaves it until it changes.",
    },
    {
      value: "retry",
      label: "Try it again",
      detail: "Weir processes it on the next pass.",
    },
  ];
}

/**
 * History's remove dialog for a failed or rejected title whose file is still in the watched folder (#785):
 * delete the download (naming whichever manager will do it, or saying Weir will delete it itself), keep it
 * without processing it again, or try it again. A title that does not qualify for this — finished, or its file
 * already gone — keeps the plain confirm in {@link HistoryFileActions} instead of this dialog.
 */
export function HistoryRemoveDialog({
  fileName,
  options,
  busy,
  error,
  onCancel,
  onConfirm,
}: {
  fileName: string;
  options: ProcessingFileRemoveOptions;
  busy: boolean;
  error: string | null;
  onCancel: () => void;
  onConfirm: (resolution: HistoryRemoveChoice) => void;
}) {
  const [choice, setChoice] = useState<HistoryRemoveChoice>("delete");

  return (
    <ConfirmDialog
      testId="history-remove-dialog"
      title={`Remove "${fileName}" from History?`}
      confirmLabel="Remove"
      cancelLabel="Cancel"
      busy={busy}
      busyLabel="Removing…"
      error={error}
      onCancel={onCancel}
      onConfirm={() => onConfirm(choice)}
      description={
        <fieldset>
          <legend className="mb-2">
            Its file is still in the watched folder.
          </legend>
          {choices(options).map((opt) => (
            <label
              key={opt.value}
              data-testid={`history-remove-dialog-choice-${opt.value}`}
              className={`mm-dialog-choice${choice === opt.value ? " mm-dialog-choice--chosen" : ""}`}
            >
              <input
                type="radio"
                name="history-remove-resolution"
                value={opt.value}
                checked={choice === opt.value}
                onChange={() => setChoice(opt.value)}
                className="mt-0.5 h-4 w-4 shrink-0 accent-mm-accent"
              />
              <span className="min-w-0">
                <span className="block font-medium">{opt.label}</span>
                <span className="block text-xs text-mm-text3">
                  {opt.detail}
                </span>
              </span>
            </label>
          ))}
        </fieldset>
      }
    />
  );
}
