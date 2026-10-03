import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type RefObject,
} from "react";

import { fitPageTabs, type PageTabsFit } from "./page-tabs-fit";

type TabsMeasure = {
  widths: readonly number[];
  more: number;
  room: number;
};

type UsePageTabsFitOptions = {
  /** Every tab's label, in order. The widths are measured again when these change. */
  labels: readonly string[];
  /** The chosen tab, or -1. */
  selectedIndex: number;
  /** False where the tabs are never folded; nothing is measured then and every tab shows. */
  enabled: boolean;
};

export type PageTabsFitting = {
  /** The box the tabs may fill. */
  rootRef: RefObject<HTMLDivElement | null>;
  /** Holds a copy of every tab, in order, drawn out of sight only to be measured. */
  layerRef: RefObject<HTMLDivElement | null>;
  /** A copy of the More button, drawn out of sight only to be measured. */
  probeRef: RefObject<HTMLSpanElement | null>;
  fit: PageTabsFit;
};

/**
 * Widths are read to the browser's own precision, not rounded: the box is as wide as all the tabs when they have the
 * room, so rounding the tabs up and the box down would fold a row that fits, and the fold would change the box's
 * width and unfold it again. This slack is for the rounding of the browser's own layout units.
 */
const LAYOUT_ROUNDING_SLACK = 0.5;

function widthOf(element: Element): number {
  return element.getBoundingClientRect().width;
}

/**
 * Which of a row of tabs show and which fold into More. Widths are measured in a hidden copy before paint, and
 * again when the room changes, the fonts arrive or the labels change. With no measured room, every tab shows.
 */
export function usePageTabsFit({
  labels,
  selectedIndex,
  enabled,
}: UsePageTabsFitOptions): PageTabsFitting {
  const rootRef = useRef<HTMLDivElement | null>(null);
  const layerRef = useRef<HTMLDivElement | null>(null);
  const probeRef = useRef<HTMLSpanElement | null>(null);
  const [measure, setMeasure] = useState<TabsMeasure | null>(null);
  const signature = labels.join("\u0000");

  const measureNow = useCallback(() => {
    const root = rootRef.current;
    const layer = layerRef.current;
    const probe = probeRef.current;
    if (!root || !layer || !probe) return;
    const widths = Array.from(layer.children, widthOf);
    const next = {
      widths,
      more: widthOf(probe),
      room: widthOf(root),
    };
    setMeasure((current) =>
      current &&
      current.room === next.room &&
      current.more === next.more &&
      current.widths.length === widths.length &&
      current.widths.every((width, index) => width === widths[index])
        ? current
        : next,
    );
  }, []);

  useLayoutEffect(() => {
    if (enabled) measureNow();
  }, [enabled, measureNow, signature]);

  useEffect(() => {
    const root = rootRef.current;
    if (!enabled || !root) return undefined;
    const observer =
      typeof ResizeObserver === "undefined"
        ? null
        : new ResizeObserver(measureNow);
    observer?.observe(root);
    const fonts = document.fonts;
    void fonts?.ready.then(measureNow);
    fonts?.addEventListener("loadingdone", measureNow);
    return () => {
      observer?.disconnect();
      fonts?.removeEventListener("loadingdone", measureNow);
    };
  }, [enabled, measureNow]);

  const count = labels.length;
  const fit = useMemo<PageTabsFit>(() => {
    const everyTab = {
      visible: Array.from({ length: count }, (_, index) => index),
      folded: [],
    };
    if (!enabled || !measure || measure.room <= 0) return everyTab;
    if (measure.widths.length !== count) return everyTab;
    return fitPageTabs(
      measure.widths,
      measure.room + LAYOUT_ROUNDING_SLACK,
      measure.more,
      selectedIndex,
    );
  }, [enabled, measure, count, selectedIndex]);

  return { rootRef, layerRef, probeRef, fit };
}
