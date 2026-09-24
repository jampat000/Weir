import { ConfirmDialog } from "../../../../components/ui/confirm-dialog";
import { errorMessage } from "../../../../lib/api/error-message";
import type { ProcessingLibrary } from "../../../../lib/processing/libraries-api";
import type { useDeleteProcessingLibrary } from "../../../../lib/processing/libraries-queries";

/**
 * Remove asks first (#691): a library's settings and hours go with it. The server refuses while work
 * is in flight and says how much, so that reason is shown in the dialog.
 */
export function RemoveLibraryDialog({
  library,
  remove,
  onClose,
}: {
  library: ProcessingLibrary;
  remove: ReturnType<typeof useDeleteProcessingLibrary>;
  onClose: () => void;
}) {
  const folder = library.watched_folder.trim() || "its folder";
  return (
    <ConfirmDialog
      testId="processing-library-remove-confirm"
      title={`Remove ${library.name}?`}
      description={
        <p>
          Weir stops watching {folder}. Its settings and hours go with it. No
          media file is touched. This cannot be undone.
        </p>
      }
      confirmLabel="Remove library"
      busy={remove.isPending}
      error={
        remove.isError
          ? errorMessage(remove.error, "That library could not be removed.")
          : null
      }
      onCancel={() => {
        remove.reset();
        onClose();
      }}
      onConfirm={() => remove.mutate(library.id, { onSuccess: onClose })}
    />
  );
}
