import { useState } from "react";

import { ModalDialog } from "../../../../components/ui/modal-dialog";
import type { HistoryResetResult } from "../../../../lib/settings/types";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { plural } from "../../../../lib/ui/mm-plural";

/**
 * Clearing all history cannot be undone (#469), so the dialog quotes the server's own counts and asks
 * for RESET to be typed before the button works.
 */
export function ClearHistoryDialog({
  preview,
  busy,
  error,
  onCancel,
  onConfirm,
}: {
  preview: HistoryResetResult;
  busy: boolean;
  error: string | null;
  onCancel: () => void;
  onConfirm: (confirm: string) => void;
}) {
  const [typed, setTyped] = useState("");
  const ready = typed.trim().toUpperCase() === "RESET";
  return (
    <ModalDialog
      title="Clear all history?"
      testId="activity-clear-all-history-dialog"
      onClose={onCancel}
      busy={busy}
    >
      <p className="mt-2 text-sm text-mm-text2">This permanently removes:</p>
      <ul className="mt-2 list-inside list-disc space-y-1 text-sm text-mm-text1">
        <li>
          {plural(
            preview.activity_events_deleted,
            "Activity event",
            "Activity events",
          )}
        </li>
        <li>{plural(preview.jobs_deleted, "finished job", "finished jobs")}</li>
      </ul>
      <p className="mt-3 text-sm leading-6 text-mm-text2">
        Queued and running work is kept, and so are all settings. No media file
        is touched. This cannot be undone.
      </p>
      <label className="mt-3 block text-sm text-mm-text2">
        <span className="mb-1.5 block text-sm text-mm-text2">
          Type RESET to confirm
        </span>
        <input
          type="text"
          className="mm-input w-full"
          value={typed}
          disabled={busy}
          onChange={(e) => setTyped(e.target.value)}
        />
      </label>
      {error ? (
        <p className="mm-modal__error" role="alert">
          {error}
        </p>
      ) : null}
      <div className="mm-modal__actions">
        <button
          type="button"
          className={mmActionButtonClass({ variant: "secondary" })}
          onClick={onCancel}
          disabled={busy}
        >
          Keep history
        </button>
        <button
          type="button"
          className={mmActionButtonClass({ variant: "primary" })}
          disabled={busy || !ready}
          onClick={() => onConfirm(typed.trim())}
        >
          {busy ? "Clearing…" : "Clear all history"}
        </button>
      </div>
    </ModalDialog>
  );
}
