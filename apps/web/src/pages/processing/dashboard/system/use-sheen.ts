import { useEffect, useRef, useState } from "react";

/** How long a sheen sweeps. */
export const SHEEN_MS = 1400;

/**
 * Whether a sheen is sweeping: true for a moment each time `trigger` changes to a new value. The first value it is
 * given starts nothing, because nothing has been read again yet; a null trigger starts nothing either.
 */
export function useSheen(trigger: number | null): boolean {
  const [sweeping, setSweeping] = useState(false);
  const previous = useRef<number | null>(null);
  useEffect(() => {
    if (trigger === null) return undefined;
    const before = previous.current;
    previous.current = trigger;
    if (before === null || before === trigger) return undefined;
    setSweeping(true);
    const stop = window.setTimeout(() => setSweeping(false), SHEEN_MS);
    return () => window.clearTimeout(stop);
  }, [trigger]);
  return sweeping;
}
