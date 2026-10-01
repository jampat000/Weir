import { useEffect } from "react";

/** The class the document carries while its window is being resized; the Pipeline's stylesheet stops every animation under it. */
export const RESIZING_CLASS = "mm-pipeline-resizing";

/** How long after the last change of the window's size it still counts as being resized. */
const RESIZE_SETTLE_MS = 350;

/**
 * Nothing on the Pipeline animates or transitions while the window is being resized: the layout snaps,
 * then the motion comes back a moment after the size stops changing.
 */
export function useStillWhileResizing(): void {
  useEffect(() => {
    let width = window.innerWidth;
    let height = window.innerHeight;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const check = () => {
      if (window.innerWidth === width && window.innerHeight === height) return;
      width = window.innerWidth;
      height = window.innerHeight;
      document.body.classList.add(RESIZING_CLASS);
      if (timer !== undefined) clearTimeout(timer);
      timer = setTimeout(
        () => document.body.classList.remove(RESIZING_CLASS),
        RESIZE_SETTLE_MS,
      );
    };
    window.addEventListener("resize", check);
    const observer =
      typeof ResizeObserver === "undefined" ? null : new ResizeObserver(check);
    observer?.observe(document.documentElement);
    return () => {
      window.removeEventListener("resize", check);
      observer?.disconnect();
      if (timer !== undefined) clearTimeout(timer);
      document.body.classList.remove(RESIZING_CLASS);
    };
  }, []);
}
