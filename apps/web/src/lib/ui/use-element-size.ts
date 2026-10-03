import { useLayoutEffect, useRef, useState, type RefObject } from "react";

export type ElementSize = { width: number; height: number };

const UNMEASURED: ElementSize = { width: 0, height: 0 };

/**
 * The size of an element in whole pixels, measured before paint and again whenever it changes or the
 * fonts arrive. It is 0 by 0 until measured, and stays so where the browser has no ResizeObserver.
 */
export function useElementSize<T extends HTMLElement>(): [
  RefObject<T | null>,
  ElementSize,
] {
  const ref = useRef<T | null>(null);
  const [size, setSize] = useState(UNMEASURED);
  useLayoutEffect(() => {
    const element = ref.current;
    if (!element || typeof ResizeObserver === "undefined") return undefined;
    let live = true;
    const read = () => {
      if (!live) return;
      const box = element.getBoundingClientRect();
      const next = {
        width: Math.round(box.width),
        height: Math.round(box.height),
      };
      setSize((current) =>
        current.width === next.width && current.height === next.height
          ? current
          : next,
      );
    };
    read();
    const observer = new ResizeObserver(read);
    observer.observe(element);
    void document.fonts?.ready.then(read);
    return () => {
      live = false;
      observer.disconnect();
    };
  }, []);
  return [ref, size];
}
