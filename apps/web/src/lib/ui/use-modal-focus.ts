import { useEffect, useRef, type RefObject } from "react";

/**
 * Keyboard behaviour for anything modal. On open, focus moves to `initialFocus` when given (a
 * confirmation points it at the safe choice), otherwise to the returned ref's element; Escape closes
 * unless `busy`; on close, focus goes back to whatever had it, if that is still on the page.
 * `lockScroll` stops the page behind a slide-over from scrolling.
 */
export function useModalFocus<T extends HTMLElement>({
  open = true,
  onClose,
  busy = false,
  lockScroll = false,
  initialFocus,
}: {
  open?: boolean;
  onClose: () => void;
  busy?: boolean;
  lockScroll?: boolean;
  initialFocus?: RefObject<HTMLElement | null>;
}): RefObject<T | null> {
  const target = useRef<T>(null);
  // Read through refs, so a new onClose each render neither re-subscribes nor re-runs the focus move.
  const close = useRef(onClose);
  const isBusy = useRef(busy);
  useEffect(() => {
    close.current = onClose;
    isBusy.current = busy;
  });

  useEffect(() => {
    if (!open) return undefined;
    const returnTo = document.activeElement;
    (initialFocus?.current ?? target.current)?.focus();
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !isBusy.current) close.current();
    };
    document.addEventListener("keydown", onKey);
    if (lockScroll) document.body.classList.add("mm-drawer-open");
    return () => {
      document.removeEventListener("keydown", onKey);
      if (lockScroll) document.body.classList.remove("mm-drawer-open");
      if (returnTo instanceof HTMLElement && returnTo.isConnected) {
        returnTo.focus();
      }
    };
  }, [open, lockScroll, initialFocus]);

  return target;
}
