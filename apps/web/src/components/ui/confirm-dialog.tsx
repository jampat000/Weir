/**
 * The confirmation in front of an action that cannot be undone (#599).
 *
 * Focus lands on the cancel button, never on the confirming one, so Enter on a freshly opened dialog
 * keeps things as they are. Escape cancels, except mid-request, when the cancel button is disabled too.
 *
 * `title` must name the thing acted on: "Remove this connection?" is useless with three on screen.
 */
import { useId, useRef, type ReactNode } from "react";

import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import { ModalDialog } from "./modal-dialog";

export function ConfirmDialog({
  title,
  description,
  confirmLabel,
  cancelLabel = "Keep it",
  busy = false,
  busyLabel = "Removing…",
  error = null,
  testId,
  onCancel,
  onConfirm,
}: {
  /** Names the item, e.g. "Remove Deluno?". */
  title: string;
  /** What confirming actually does, and whether it can be undone. */
  description?: ReactNode;
  confirmLabel: string;
  cancelLabel?: string;
  busy?: boolean;
  busyLabel?: string;
  error?: string | null;
  testId: string;
  onCancel: () => void;
  onConfirm: () => void;
}) {
  const descriptionId = useId();
  const cancelRef = useRef<HTMLButtonElement>(null);

  return (
    <ModalDialog
      title={title}
      testId={testId}
      onClose={onCancel}
      busy={busy}
      describedBy={description ? descriptionId : undefined}
      initialFocus={cancelRef}
    >
      {description ? (
        <div id={descriptionId} className="mm-modal__body">
          {description}
        </div>
      ) : null}
      {error ? (
        <p className="mm-modal__error" role="alert">
          {error}
        </p>
      ) : null}
      <div className="mm-modal__actions">
        <button
          type="button"
          ref={cancelRef}
          data-testid={`${testId}-cancel`}
          className={mmActionButtonClass({ variant: "secondary" })}
          onClick={onCancel}
          disabled={busy}
        >
          {cancelLabel}
        </button>
        <button
          type="button"
          data-testid={`${testId}-confirm`}
          className={mmActionButtonClass({ variant: "primary" })}
          disabled={busy}
          onClick={onConfirm}
        >
          {busy ? busyLabel : confirmLabel}
        </button>
      </div>
    </ModalDialog>
  );
}
