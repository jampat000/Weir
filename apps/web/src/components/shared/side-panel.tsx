/**
 * A panel that slides over the page from the right, for editing or reading one thing without losing the page
 * behind it. Close it and you are exactly where you were.
 *
 * It exists because the library editor opened *below* the list: on a long Settings page the screen did not move,
 * so clicking Edit looked like nothing happening at all. Anything that used to open below the fold belongs here.
 * Escape closes it, focus moves into it and returns to whatever opened it, and the page behind it does not scroll.
 */
import { useEffect, useRef } from "react";

export function SidePanel({
  open,
  title,
  eyebrow,
  subtitle,
  onClose,
  children,
  dataTestId,
}: {
  open: boolean;
  title: string;
  eyebrow?: string;
  /** One quiet line under the title: a path, a count, what this is. */
  subtitle?: string;
  onClose: () => void;
  children: React.ReactNode;
  dataTestId?: string;
}): React.ReactElement | null {
  const panel = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return undefined;
    const returnTo = document.activeElement;
    panel.current?.focus();
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKey);
    document.body.classList.add("mm-drawer-open");
    return () => {
      document.removeEventListener("keydown", onKey);
      document.body.classList.remove("mm-drawer-open");
      if (returnTo instanceof HTMLElement) returnTo.focus();
    };
  }, [open, onClose]);

  if (!open) return null;

  return (
    <>
      <button
        type="button"
        className="mm-drawer-backdrop"
        aria-label="Close"
        onClick={onClose}
      />
      <section
        className="mm-drawer"
        role="dialog"
        aria-modal="true"
        aria-label={title}
        tabIndex={-1}
        ref={panel}
        data-testid={dataTestId}
      >
        <header className="mm-drawer__head">
          <div className="mm-drawer__titles">
            {eyebrow ? <p className="mm-drawer__eyebrow">{eyebrow}</p> : null}
            <h2 className="mm-drawer__title">{title}</h2>
            {subtitle ? <p className="mm-drawer__path">{subtitle}</p> : null}
          </div>
          <button
            type="button"
            className="mm-head-control mm-head-control--icon"
            aria-label="Close"
            onClick={onClose}
          >
            ×
          </button>
        </header>
        <div className="mm-drawer__body">{children}</div>
      </section>
    </>
  );
}
