import { useEffect, useState } from "react";

import { useLiveConnection } from "./live-connection";

/**
 * How long the very first connection may take before a failure to make it is worth saying. A page that has just loaded
 * is still connecting, and a connection that drops after it was up is news at once.
 */
export const FIRST_CONNECTION_GRACE_MS = 5_000;

/**
 * How long a connection that was up must stay lost before it is worth saying, so a blip that mends itself shows nothing.
 * When the browser already knows it is offline, there is nothing to wait for.
 */
export const LOST_CONNECTION_GRACE_MS = 3_000;

/**
 * Whether to tell the person that live updates have stopped: the connection that was up has stayed lost for a few seconds
 * (at once when the browser is offline), or the first one has failed for longer than the grace. Never while the page is
 * simply still connecting.
 */
export function useLiveUpdatesPaused(): boolean {
  const { status, hasBeenLive } = useLiveConnection();
  const [graceOver, setGraceOver] = useState(false);
  const [lostLongEnough, setLostLongEnough] = useState(false);
  const waitingForFirst = status !== "live" && !hasBeenLive;

  useEffect(() => {
    if (!waitingForFirst) return;
    const timer = window.setTimeout(
      () => setGraceOver(true),
      FIRST_CONNECTION_GRACE_MS,
    );
    return () => window.clearTimeout(timer);
  }, [waitingForFirst]);

  useEffect(() => {
    if (status !== "lost" || !hasBeenLive) return;
    const timer = window.setTimeout(
      () => setLostLongEnough(true),
      navigator.onLine ? LOST_CONNECTION_GRACE_MS : 0,
    );
    return () => {
      window.clearTimeout(timer);
      setLostLongEnough(false);
    };
  }, [status, hasBeenLive]);

  return status === "lost" && (hasBeenLive ? lostLongEnough : graceOver);
}
