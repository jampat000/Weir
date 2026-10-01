import { useState, type ReactNode } from "react";

import { errorMessage } from "../../lib/api/error-message";
import type { ProcessingFileRemoveOptions } from "../../lib/processing/files-api";
import {
  useForgetProcessingFile,
  useProcessingFileRemoveOptions,
} from "../../lib/processing/files-queries";
import { HistoryRemoveDialog } from "./history-remove-dialog";

type FileRemoval = {
  /** Asks what removing this file would do, then removes it or opens the dialog that asks the person. */
  start: () => Promise<void>;
  /** Weir is asking what removing would do. */
  checking: boolean;
  /** The removal itself is running. */
  removing: boolean;
  /** The remove dialog while it is open, else nothing. */
  dialog: ReactNode;
};

/**
 * Removing one title from History (#785): a title whose file is still in the watched folder asks first, in
 * {@link HistoryRemoveDialog}, whether to delete it, keep it, try it again or only drop it from the list; any other
 * title is simply forgotten. The file's own screen and every other place that offers "Remove" share this flow.
 */
export function useFileRemoval({
  fileId,
  fileName,
  onRemoved,
  onProblem,
}: {
  fileId: number;
  fileName: string;
  /** What happened, once the title is gone. */
  onRemoved: (message: string) => void;
  /** Why it could not be removed. */
  onProblem: (message: string) => void;
}): FileRemoval {
  const forget = useForgetProcessingFile();
  const removeOptions = useProcessingFileRemoveOptions();
  const [dialogOptions, setDialogOptions] =
    useState<ProcessingFileRemoveOptions | null>(null);
  const [dialogError, setDialogError] = useState<string | null>(null);

  /** The plain remove every title without a choice keeps: forget the row, touch nothing else. */
  async function removePlain() {
    try {
      await forget.mutateAsync({ id: fileId });
      onRemoved("Removed from the list. The file on disk is untouched.");
    } catch (error) {
      onProblem(
        errorMessage(error, "That file could not be removed from the list."),
      );
    }
  }

  async function start() {
    try {
      const options = await removeOptions.mutateAsync(fileId);
      if (options.requires_choice) {
        setDialogError(null);
        setDialogOptions(options);
        return;
      }
    } catch (error) {
      onProblem(
        errorMessage(
          error,
          "Weir could not check what removing this file would do.",
        ),
      );
      return;
    }
    await removePlain();
  }

  async function confirm(resolution: "remove" | "delete" | "keep" | "retry") {
    setDialogError(null);
    try {
      // What the dialog showed for a title with no recorded fingerprint (#786 follow-up), echoed back so the
      // server can check the file still matches before delete or keep touch it. Harmless to send for retry or a
      // plain remove, which never look at it.
      const confirmed =
        dialogOptions &&
        !dialogOptions.fingerprint_recorded &&
        dialogOptions.unconfirmed_size_bytes != null &&
        dialogOptions.unconfirmed_modified_at != null
          ? {
              size_bytes: dialogOptions.unconfirmed_size_bytes,
              modified_at: dialogOptions.unconfirmed_modified_at,
            }
          : undefined;
      const result = await forget.mutateAsync({
        id: fileId,
        resolution,
        confirm: confirmed,
      });
      setDialogOptions(null);
      onRemoved(result?.detail ?? "Done.");
    } catch (error) {
      setDialogError(errorMessage(error, "That file could not be removed."));
    }
  }

  return {
    start,
    checking: removeOptions.isPending,
    removing: forget.isPending,
    dialog: dialogOptions ? (
      <HistoryRemoveDialog
        fileName={fileName}
        options={dialogOptions}
        busy={forget.isPending}
        error={dialogError}
        onCancel={() => setDialogOptions(null)}
        onConfirm={(resolution) => void confirm(resolution)}
      />
    ) : null,
  };
}
