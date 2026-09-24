import { useId, type ReactNode, type RefObject } from "react";

import { useModalFocus } from "../../lib/ui/use-modal-focus";

/** The centred dialog frame: overlay, card and title, with the keyboard behaviour of useModalFocus. */
export function ModalDialog({
  title,
  testId,
  onClose,
  busy = false,
  describedBy,
  initialFocus,
  children,
}: {
  title: ReactNode;
  testId?: string;
  onClose: () => void;
  busy?: boolean;
  describedBy?: string;
  initialFocus?: RefObject<HTMLElement | null>;
  children: ReactNode;
}) {
  const titleId = useId();
  const card = useModalFocus<HTMLDivElement>({ onClose, busy, initialFocus });
  return (
    <div
      className="mm-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      aria-describedby={describedBy}
      data-testid={testId}
    >
      <div className="mm-modal__card" ref={card} tabIndex={-1}>
        <h3 id={titleId} className="mm-modal__title">
          {title}
        </h3>
        {children}
      </div>
    </div>
  );
}
