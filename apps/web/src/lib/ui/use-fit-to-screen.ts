import { useLayoutEffect, type RefObject } from "react";

/**
 * Makes an element exactly as tall as what is left of the window under it, so a page that has too much to show
 * scrolls inside its own panels rather than pushing the page down. Never shorter than `minHeight`: under that the
 * page scrolls rather than crushing what is in it. With `enabled` false (a phone, a stacked page) the element is
 * left to its content. Measured again whenever the window changes size, the fonts arrive, or anything around the
 * element changes size: a header that wraps or a banner above it moves it without a resize, and the elements that
 * hold it (the main area, the page) grow or shrink with it.
 */
export function useFitToScreen(
  ref: RefObject<HTMLElement | null>,
  enabled: boolean,
  minHeight: number,
): void {
  useLayoutEffect(() => {
    const element = ref.current;
    if (!element || !enabled) return undefined;
    let live = true;
    const fit = () => {
      if (!live) return;
      const top = element.getBoundingClientRect().top + window.scrollY;
      const main = element.closest("main");
      const below = main
        ? parseFloat(getComputedStyle(main).paddingBottom) || 0
        : 0;
      element.style.height = `${Math.max(minHeight, Math.floor(window.innerHeight - top - below))}px`;
    };
    fit();
    window.addEventListener("resize", fit);
    const observer =
      typeof ResizeObserver === "undefined" ? null : new ResizeObserver(fit);
    for (
      let holder = element.parentElement;
      holder;
      holder = holder.parentElement
    ) {
      observer?.observe(holder);
    }
    void document.fonts?.ready.then(fit);
    return () => {
      live = false;
      window.removeEventListener("resize", fit);
      observer?.disconnect();
      element.style.height = "";
    };
  }, [ref, enabled, minHeight]);
}
