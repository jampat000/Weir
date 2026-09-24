import { useEffect, useRef, type RefObject } from "react";

/** Open modals, newest last. Escape closes only the newest, so a dialog over a panel leaves the panel open. */
const openLayers: symbol[] = [];

/** Everything Tab can land on. */
const TABBABLE = [
  "a[href]",
  "button:not([disabled])",
  "input:not([disabled]):not([type='hidden'])",
  "select:not([disabled])",
  "textarea:not([disabled])",
  "summary",
  "[tabindex]:not([tabindex='-1'])",
  "[contenteditable='true']",
].join(",");

function tabbableWithin(container: HTMLElement): HTMLElement[] {
  return Array.from(container.querySelectorAll<HTMLElement>(TABBABLE)).filter(
    (element) => !element.closest("[hidden], [inert]"),
  );
}

/**
 * Keeps Tab inside a layer that declares `aria-modal`: past the last control it wraps to the first,
 * and before the first to the last. Anything not modal lets Tab leave as usual.
 */
function keepTabInside(container: HTMLElement, event: KeyboardEvent): void {
  if (!container.closest("[aria-modal='true']")) return;
  const stops = tabbableWithin(container);
  if (stops.length === 0) {
    event.preventDefault();
    container.focus();
    return;
  }
  const first = stops[0];
  const last = stops[stops.length - 1];
  const active = document.activeElement;
  const outside = !(active instanceof Node) || !container.contains(active);
  if (event.shiftKey && (outside || active === first || active === container)) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && (outside || active === last)) {
    event.preventDefault();
    first.focus();
  }
}

/**
 * Keyboard behaviour for anything modal. On open, focus moves to `initialFocus` when given (a
 * confirmation points it at the safe choice), otherwise to the returned ref's element; Tab stays
 * inside while the layer declares `aria-modal`; Escape closes unless `busy`; on close, focus goes back
 * to whatever had it, if that is still on the page. `lockScroll` stops the page behind a slide-over
 * from scrolling.
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
  // Everything but `open` is read through a ref. A parent that re-renders with a new onClose (every
  // keystroke in a form inside the panel) must not re-run the effect, or focus jumps out of the
  // field being typed in (#697).
  const latest = useRef({ onClose, busy, lockScroll, initialFocus });
  useEffect(() => {
    latest.current = { onClose, busy, lockScroll, initialFocus };
  });

  useEffect(() => {
    if (!open) return undefined;
    const returnTo = document.activeElement;
    const layer = Symbol("modal");
    const locksScroll = latest.current.lockScroll;
    openLayers.push(layer);
    (latest.current.initialFocus?.current ?? target.current)?.focus();
    const onKey = (event: KeyboardEvent) => {
      if (openLayers[openLayers.length - 1] !== layer) return;
      if (event.key === "Escape" && !latest.current.busy) {
        latest.current.onClose();
      } else if (event.key === "Tab" && target.current) {
        keepTabInside(target.current, event);
      }
    };
    document.addEventListener("keydown", onKey);
    if (locksScroll) document.body.classList.add("mm-drawer-open");
    return () => {
      openLayers.splice(openLayers.indexOf(layer), 1);
      document.removeEventListener("keydown", onKey);
      if (locksScroll) document.body.classList.remove("mm-drawer-open");
      if (returnTo instanceof HTMLElement && returnTo.isConnected) {
        returnTo.focus();
      }
    };
  }, [open]);

  return target;
}
