import { useEffect, useState } from "react";

/**
 * Updates as they happen, but calmly: fresh data only lands in the list while the reader is at the top with nothing
 * held open (a row they are reading, older pages they have loaded). Otherwise the list they are reading stays put, and the
 * caller offers the new data with a button.
 */
export function useCalmFeed<T>(
  live: T | undefined,
  dataKey: string,
  holding: boolean,
) {
  const [snapshot, setSnapshot] = useState<{ key: string; data: T } | null>(
    null,
  );
  const [atTop, setAtTop] = useState(true);
  const [feed, setFeed] = useState<HTMLElement | null>(null);
  const hasData = live !== undefined;

  useEffect(() => {
    const evaluate = () =>
      setAtTop(!feed || feed.getBoundingClientRect().top >= 0);
    evaluate();
    window.addEventListener("scroll", evaluate, true);
    return () => window.removeEventListener("scroll", evaluate, true);
  }, [hasData, feed]);

  const calm = atTop && !holding;

  useEffect(() => {
    if (live === undefined) return;
    setSnapshot((prev) =>
      prev && prev.key === dataKey && (prev.data === live || !calm)
        ? prev
        : { key: dataKey, data: live },
    );
  }, [live, dataKey, calm]);

  return {
    /** The element whose top says whether the reader is at the top of the list: the callback that hands it over. */
    watchFeed: setFeed,
    /** What the list shows: the held snapshot, or the live data before one exists. */
    shown: snapshot && snapshot.key === dataKey ? snapshot.data : live,
    /** Show this data now, whatever the reader is doing. */
    show: (data: T) => setSnapshot({ key: dataKey, data }),
  };
}
