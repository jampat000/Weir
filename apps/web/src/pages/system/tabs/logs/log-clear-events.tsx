import { useState } from "react";

import { errorMessage } from "../../../../lib/api/error-message";
import { useHistoryResetMutation } from "../../../../lib/settings/queries";
import { fetchOperationalHistoryPreview } from "../../../../lib/settings/settings-api";
import type { HistoryResetResult } from "../../../../lib/settings/types";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { plural } from "../../../../lib/ui/mm-plural";
import { ClearEventsDialog } from "./clear-events-dialog";

/**
 * "Clear events…" in the Log card's header: it clears the Activity events and the finished jobs, and nothing else: not
 * the server log, queued or running work, settings or any media file. It says what it would remove, and asks for RESET,
 * before it does.
 */
export function LogClearEvents({
  onCleared,
  onProblem,
}: {
  /** Called with a sentence saying what was removed, once it has been. */
  onCleared: (notice: string) => void;
  /** Called with a sentence saying what went wrong, or with null when a new try begins. */
  onProblem: (problem: string | null) => void;
}) {
  const [preview, setPreview] = useState<HistoryResetResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const reset = useHistoryResetMutation();

  async function start(): Promise<void> {
    onProblem(null);
    setError(null);
    try {
      setPreview(await fetchOperationalHistoryPreview());
    } catch (failure) {
      onProblem(
        errorMessage(
          failure,
          "Could not check what clearing events would remove.",
        ),
      );
    }
  }

  async function confirm(typed: string): Promise<void> {
    setError(null);
    try {
      const out = await reset.mutateAsync(typed);
      setPreview(null);
      onCleared(
        `Events cleared. Removed ${plural(out.activity_events_deleted, "Activity event", "Activity events")} and ${plural(out.jobs_deleted, "finished job", "finished jobs")}. No media file was touched.`,
      );
    } catch (failure) {
      setError(errorMessage(failure, "Could not clear events."));
    }
  }

  return (
    <>
      <button
        type="button"
        className={`${mmActionButtonClass({ variant: "danger-outline" })} mm-sys-btn`}
        data-testid="logs-clear-events"
        onClick={() => void start()}
      >
        Clear events…
      </button>
      {preview ? (
        <ClearEventsDialog
          preview={preview}
          busy={reset.isPending}
          error={error}
          onCancel={() => setPreview(null)}
          onConfirm={(typed) => void confirm(typed)}
        />
      ) : null}
    </>
  );
}
