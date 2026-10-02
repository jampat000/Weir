import { useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { FILE_HISTORY_MAX_DAYS } from "../../../../lib/processing/file-history-retention";
import { processingKeys } from "../../../../lib/processing/query-keys";
import {
  useProcessingOperatorSettingsQuery,
  useProcessingOperatorSettingsSaveMutation,
} from "../../../../lib/processing/queries";

/**
 * How long a file's activity outlives the file: a file's activity is kept for as long as Weir still knows the file, and
 * this is the number of days it is kept after the file is gone or forgotten. The number is a draft until `save`.
 */
export function useFileActivityRetention() {
  const settings = useProcessingOperatorSettingsQuery();
  const saveMutation = useProcessingOperatorSettingsSaveMutation();
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState<string | null>(null);

  const savedDays = settings.data?.file_log_retention_days;
  const shown = draft ?? (savedDays === undefined ? "" : String(savedDays));
  const days = Number(shown);
  const valid =
    shown.trim() !== "" &&
    Number.isInteger(days) &&
    days >= 0 &&
    days <= FILE_HISTORY_MAX_DAYS;

  return {
    /** "unreadable" when the server would not say, "loading" until it has, otherwise "ready". */
    status: settings.isError
      ? ("unreadable" as const)
      : settings.data
        ? ("ready" as const)
        : ("loading" as const),
    shown,
    setDraft,
    valid,
    dirty: draft !== null && days !== savedDays,
    saving: saveMutation.isPending,
    error: saveMutation.isError ? saveMutation.error : null,
    maxDays: FILE_HISTORY_MAX_DAYS,
    /** Saves the number; `onSaved` runs once the server has taken it. */
    save: (onSaved: () => void) =>
      saveMutation.mutate(
        { file_log_retention_days: days },
        {
          onSuccess: () => {
            setDraft(null);
            void queryClient.invalidateQueries({
              queryKey: processingKeys.files,
            });
            onSaved();
          },
        },
      ),
  };
}

export type FileActivityRetention = ReturnType<typeof useFileActivityRetention>;
