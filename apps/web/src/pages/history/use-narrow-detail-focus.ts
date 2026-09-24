import { useEffect, useRef } from "react";

/** Below this width the detail sits under the list rather than beside it (#687). */
const NARROW_DETAIL_WIDTH = "(max-width: 1099.98px)";

/**
 * Scrolls a narrow-width detail panel into view and focuses its title whenever `id` changes, so picking
 * a row has a visible result even when the detail sits below the list rather than beside it (#687). Skips
 * the page's first render, so loading History never steals focus from wherever the browser put it.
 */
export function useNarrowDetailFocus(id: string | number) {
  const sectionRef = useRef<HTMLElement>(null);
  const titleRef = useRef<HTMLHeadingElement>(null);
  const openedBefore = useRef(false);

  useEffect(() => {
    if (!openedBefore.current) {
      openedBefore.current = true;
      return;
    }
    if (!window.matchMedia(NARROW_DETAIL_WIDTH).matches) return;
    sectionRef.current?.scrollIntoView({ block: "start" });
    titleRef.current?.focus();
  }, [id]);

  return { sectionRef, titleRef };
}
