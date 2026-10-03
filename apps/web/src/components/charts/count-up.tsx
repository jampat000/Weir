import { useEffect, useRef, useState } from "react";

import { motionAllowed } from "../../lib/ui/motion-allowed";
import { windowIsResizing } from "../../lib/ui/resizing-class";
import { COUNT_UP_MS, countUpValue } from "./count-up-math";

type CountUpProps = {
  value: number;
  /** Writes the number as it counts: the figure's decimals and grouping. */
  format: (value: number) => string;
  durationMs?: number;
};

/**
 * A figure that counts to its new value rather than jumping. It shows its first value at once, and shows every value
 * at once when motion is not wanted: reduced motion, a tab nobody is looking at, or a window being resized.
 */
export function CountUp({
  value,
  format,
  durationMs = COUNT_UP_MS,
}: CountUpProps) {
  const [shown, setShown] = useState(value);
  const shownRef = useRef(value);
  useEffect(() => {
    const from = shownRef.current;
    if (from === value) return undefined;
    if (!motionAllowed() || windowIsResizing()) {
      shownRef.current = value;
      setShown(value);
      return undefined;
    }
    const start = performance.now();
    let handle = 0;
    const step = (time: number) => {
      const elapsed = time - start;
      const next = countUpValue(from, value, elapsed, durationMs);
      shownRef.current = next;
      setShown(next);
      if (elapsed < durationMs) handle = requestAnimationFrame(step);
    };
    handle = requestAnimationFrame(step);
    return () => cancelAnimationFrame(handle);
  }, [value, durationMs]);
  return <>{format(shown)}</>;
}
