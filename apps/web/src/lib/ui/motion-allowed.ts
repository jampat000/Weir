/**
 * Whether something may be shown as movement right now. A tab nobody is looking at has its animations
 * paused by the browser and replays them when it is shown again, so a card that finished an hour ago
 * would fly across the page on return; and a person who asks for reduced motion gets none.
 */
export function motionAllowed(): boolean {
  if (typeof document !== "undefined" && document.visibilityState !== "visible")
    return false;
  if (
    typeof window !== "undefined" &&
    window.matchMedia?.("(prefers-reduced-motion: reduce)").matches
  )
    return false;
  return true;
}
