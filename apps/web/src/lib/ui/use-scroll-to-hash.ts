import { useEffect } from "react";
import { useLocation } from "react-router-dom";

/** How long a page may keep loading above the target, and the target still be brought into view. */
const SETTLE_MS = 2000;

/** What the person does that means they are scrolling for themselves now. */
const USER_SCROLL_EVENTS = ["wheel", "touchstart", "keydown"] as const;

/**
 * Brings the element the address's `#fragment` names into view when the calling page appears. A list above the
 * target is usually still loading then, which moves the target further down, so it is brought into view again as the
 * page grows, until the person scrolls or a moment has passed.
 */
export function useScrollToHash(): void {
  const { hash } = useLocation();
  useEffect(() => {
    if (!hash) return undefined;
    const id = decodeURIComponent(hash.slice(1));
    const bringIntoView = () =>
      document.getElementById(id)?.scrollIntoView({ block: "center" });
    bringIntoView();
    if (typeof ResizeObserver === "undefined") return undefined;

    const observer = new ResizeObserver(bringIntoView);
    observer.observe(document.body);
    const stop = () => observer.disconnect();
    const timer = window.setTimeout(stop, SETTLE_MS);
    for (const name of USER_SCROLL_EVENTS) {
      window.addEventListener(name, stop, { once: true });
    }
    return () => {
      window.clearTimeout(timer);
      stop();
      for (const name of USER_SCROLL_EVENTS) {
        window.removeEventListener(name, stop);
      }
    };
  }, [hash]);
}
