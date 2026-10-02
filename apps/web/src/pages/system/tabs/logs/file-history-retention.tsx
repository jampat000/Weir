import { useQueryClient } from "@tanstack/react-query";
import { useId, useState } from "react";

import { errorMessage } from "../../../../lib/api/error-message";
import { FILE_HISTORY_MAX_DAYS } from "../../../../lib/processing/file-history-retention";
import { processingKeys } from "../../../../lib/processing/query-keys";
import {
  useProcessingOperatorSettingsQuery,
  useProcessingOperatorSettingsSaveMutation,
} from "../../../../lib/processing/queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { plural } from "../../../../lib/ui/mm-plural";

/**
 * How long a file's history outlives the file. A file's history is kept for as long as Weir still knows the
 * file; this is the number of days it is kept after the file is gone or forgotten. Operators and admins change
 * it here, with the other retention settings; a viewer reads it.
 */
export function FileHistoryRetentionSetting({
  editable,
}: {
  editable: boolean;
}) {
  const settings = useProcessingOperatorSettingsQuery();
  const save = useProcessingOperatorSettingsSaveMutation();
  const queryClient = useQueryClient();
  const inputId = useId();
  const [draft, setDraft] = useState<string | null>(null);
  const [saved, setSaved] = useState<string | null>(null);

  if (settings.isError) {
    return (
      <section className="mm-history-retention">
        <p className="mm-history-note" role="alert">
          Weir could not read how long a file&rsquo;s history is kept. Refresh
          the page to try again.
        </p>
      </section>
    );
  }
  if (!settings.data) return null;

  const days = settings.data.file_log_retention_days;
  const shown = draft ?? String(days);
  const value = Number(shown);
  const valid =
    shown.trim() !== "" &&
    Number.isInteger(value) &&
    value >= 0 &&
    value <= FILE_HISTORY_MAX_DAYS;
  const dirty = draft !== null && value !== days;

  if (!editable) {
    return (
      <section className="mm-history-retention" data-testid="history-retention">
        <p className="mm-history-note">
          {days > 0
            ? `Weir keeps a file’s history for ${plural(days, "day", "days")} after it’s gone.`
            : "Weir keeps a file’s history until it is removed."}
        </p>
      </section>
    );
  }

  return (
    <section
      className="mm-history-retention"
      aria-label="How long a file's history is kept"
      data-testid="history-retention"
    >
      <div className="mm-history-retention__row">
        <label htmlFor={inputId}>Keep a file’s history for</label>
        <input
          id={inputId}
          className="mm-input mm-history-retention__days"
          type="number"
          min={0}
          max={FILE_HISTORY_MAX_DAYS}
          value={shown}
          disabled={save.isPending}
          onChange={(event) => {
            setDraft(event.target.value);
            setSaved(null);
          }}
        />
        <span>days after it’s gone</span>
        {dirty ? (
          <button
            type="button"
            className={mmActionButtonClass({ variant: "secondary" })}
            disabled={!valid || save.isPending}
            onClick={() =>
              save.mutate(
                { file_log_retention_days: value },
                {
                  onSuccess: () => {
                    setDraft(null);
                    setSaved(
                      value > 0
                        ? `Saved. A file’s history is kept for ${plural(value, "day", "days")} after it’s gone.`
                        : "Saved. A file’s history is kept until it is removed.",
                    );
                    void queryClient.invalidateQueries({
                      queryKey: processingKeys.files,
                    });
                  },
                },
              )
            }
          >
            {save.isPending ? "Saving…" : "Save"}
          </button>
        ) : null}
      </div>
      <p className="mm-history-note">
        While Weir still knows a file, its history is kept. 0 keeps it for ever.
      </p>
      {saved ? (
        <p className="mm-history-note" role="status">
          {saved}
        </p>
      ) : null}
      {save.isError ? (
        <p className="mm-history-note mm-status-text--failed" role="alert">
          {errorMessage(save.error, "That change could not be saved.")}
        </p>
      ) : null}
    </section>
  );
}
