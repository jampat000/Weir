import { useEffect, type RefObject } from "react";

/**
 * Closes an open popover on Escape or a pointer down outside `containerRef`. For a lightweight
 * control such as a dropdown menu, not a full modal: nothing here traps Tab or moves focus, which
 * a dialog or side panel gets from {@link import("./use-modal-focus").useModalFocus} instead.
 */
export function useCloseOnOutsideAndEscape(
  open: boolean,
  onClose: () => void,
  containerRef: RefObject<HTMLElement | null>,
): void {
  useEffect(() => {
    if (!open) return undefined;
    const onPointerDown = (event: MouseEvent) => {
      const target = event.target as Node | null;
      if (
        containerRef.current &&
        target &&
        !containerRef.current.contains(target)
      ) {
        onClose();
      }
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open, onClose, containerRef]);
}
