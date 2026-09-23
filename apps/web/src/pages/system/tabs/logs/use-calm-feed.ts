import { useEffect, useRef, useState } from "react";

import type { ActivityRecentResponse } from "../../../../lib/api/types";

/**
 * Live, but calm: fresh entries only land in the list while the reader is at the top with nothing
 * opened and no older pages loaded. Otherwise the list they are reading stays put, and the caller
 * offers the new entries with a button.
 */
export function useCalmFeed(
  live: ActivityRecentResponse | undefined,
  dataKey: string,
  olderLoaded: boolean,
) {
  const [snapshot, setSnapshot] = useState<{
    key: string;
    data: ActivityRecentResponse;
  } | null>(null);
  const [calm, setCalm] = useState(true);
  const feedRef = useRef<HTMLElement | null>(null);
  const hasData = Boolean(live);

  useEffect(() => {
    const evaluate = () => {
      const feed = feedRef.current;
      const atTop = !feed || feed.getBoundingClientRect().top >= 0;
      const expanded = Boolean(feed?.querySelector("details[open]"));
      setCalm(atTop && !expanded && !olderLoaded);
    };
    evaluate();
    window.addEventListener("scroll", evaluate, true);
    document.addEventListener("toggle", evaluate, true);
    return () => {
      window.removeEventListener("scroll", evaluate, true);
      document.removeEventListener("toggle", evaluate, true);
    };
  }, [olderLoaded, hasData]);

  useEffect(() => {
    if (!live) return;
    setSnapshot((prev) =>
      prev && prev.key === dataKey && (prev.data === live || !calm)
        ? prev
        : { key: dataKey, data: live },
    );
  }, [live, dataKey, calm]);

  return {
    feedRef,
    /** What the list shows: the held snapshot, or the live data before one exists. */
    shown: snapshot && snapshot.key === dataKey ? snapshot.data : live,
    /** Show this data now, whatever the reader is doing. */
    show: (data: ActivityRecentResponse) => setSnapshot({ key: dataKey, data }),
  };
}
