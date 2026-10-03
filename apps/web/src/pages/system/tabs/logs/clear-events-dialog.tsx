import { useState } from "react";

import { ConfirmDialog } from "../../../../components/ui/confirm-dialog";
import type { HistoryResetResult } from "../../../../lib/settings/types";
import { plural } from "../../../../lib/ui/mm-plural";

/**
 * Clearing the events and finished jobs cannot be undone (#469), so the dialog quotes the server's own counts and asks
 * for RESET to be typed before the button works. The button that clears is red.
 */
export function ClearEventsDialog({
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
    <ConfirmDialog
      title="Clear events?"
      testId="logs-clear-dialog"
      tone="danger"
      confirmLabel="Clear events"
      busyLabel="Clearing…"
      cancelLabel="Keep them"
      busy={busy}
      confirmDisabled={!ready}
      error={error}
      onCancel={onCancel}
      onConfirm={() => onConfirm(typed.trim())}
      description={
        <>
          <p className="text-sm text-mm-text2">This permanently removes:</p>
          <ul className="list-inside list-disc space-y-1 text-sm text-mm-text1">
            <li>
              {plural(
                preview.activity_events_deleted,
                "Activity event",
                "Activity events",
              )}
            </li>
            <li>
              {plural(preview.jobs_deleted, "finished job", "finished jobs")}
            </li>
          </ul>
          <p className="text-sm leading-6 text-mm-text2">
            Queued and running work is kept, and so are all settings and the
            server log. No media file is touched. This cannot be undone.
          </p>
          <label className="block text-sm text-mm-text2">
            <span className="mb-1.5 block">Type RESET to confirm</span>
            <input
              type="text"
              className="mm-input w-full"
              value={typed}
              disabled={busy}
              onChange={(event) => setTyped(event.target.value)}
            />
          </label>
        </>
      }
    />
  );
}
