import { useEffect } from "react";

/**
 * The class the document carries while its window is being resized; the Dashboard's stylesheet gives every
 * animation and transition under it no time (weir-processing-dashboard.css).
 */
export const RESIZING_CLASS = "mm-pipeline-resizing";

/** How long after the last change of the window's size it still counts as being resized. */
const RESIZE_SETTLE_MS = 350;

/** The CSS animations on the page that end: the ones that run forever (a sheen, a pulse) are not stopped by the class. */
function endingAnimations(): Animation[] {
  if (typeof document.getAnimations !== "function") return [];
  return document
    .getAnimations()
    .filter(
      (animation) =>
        "animationName" in animation &&
        animation.effect?.getComputedTiming().iterations !== Infinity,
    );
}

/**
 * Takes the class off. What ran at no time under it would run its remainder once its duration is back, and a tile
 * that appeared in the middle of the resize would fade in half-way, so those animations are finished first: what
 * appeared during the resize is simply there.
 */
function endResize(): void {
  const stilled = endingAnimations();
  document.body.classList.remove(RESIZING_CLASS);
  // The styles must be recalculated without the class before the animations are finished.
  void document.body.offsetHeight;
  for (const animation of stilled) animation.finish();
}

/**
 * Nothing on the Dashboard animates or transitions while the window is being resized: the layout snaps, then
 * the motion comes back a moment after the size stops changing, without anything that had arrived arriving again.
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
      timer = setTimeout(endResize, RESIZE_SETTLE_MS);
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
