import { useEffect, type RefObject } from "react";

import { useHoldFit } from "./fit-hold";

/**
 * Closes an open popover on Escape or a pointer down outside `containerRef`. For a lightweight
 * control such as a dropdown menu, not a full modal: nothing here traps Tab or moves focus, which
 * a dialog or side panel gets from {@link import("./use-modal-focus").useModalFocus} instead. While it is open the
 * headers hold their layout (see fit-hold), so a control that is being used is not moved out from under the hand.
 */
export function useCloseOnOutsideAndEscape(
  open: boolean,
  onClose: () => void,
  containerRef: RefObject<HTMLElement | null>,
): void {
  useHoldFit(open);
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
