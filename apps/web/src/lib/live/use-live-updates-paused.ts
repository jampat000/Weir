import { useEffect, useState } from "react";

import { useLiveConnection } from "./live-connection";

/**
 * How long the very first connection may take before a failure to make it is worth saying. A page that has just loaded
 * is still connecting, and a connection that drops after it was up is news at once.
 */
export const FIRST_CONNECTION_GRACE_MS = 5_000;

/**
 * Whether to tell the person that live updates have stopped: the connection dropped after it was up, or the first one
 * has failed for longer than the grace. Never while the page is simply still connecting.
 */
export function useLiveUpdatesPaused(): boolean {
  const { status, hasBeenLive } = useLiveConnection();
  const [graceOver, setGraceOver] = useState(false);
  const waitingForFirst = status !== "live" && !hasBeenLive;

  useEffect(() => {
    if (!waitingForFirst) return;
    const timer = window.setTimeout(
      () => setGraceOver(true),
      FIRST_CONNECTION_GRACE_MS,
    );
    return () => window.clearTimeout(timer);
  }, [waitingForFirst]);

  return status === "lost" && (hasBeenLive || graceOver);
}
