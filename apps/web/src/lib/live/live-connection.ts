import { useSyncExternalStore } from "react";

/** Whether the live connection (the Activity stream) is being made, is up, or has dropped. */
export type LiveStatus = "connecting" | "live" | "lost";

export type LiveConnection = {
  status: LiveStatus;
  /** It has been up at least once since the page began watching, so a drop is news rather than a slow start. */
  hasBeenLive: boolean;
};

export type LiveConnectionEvent = "opened" | "dropped";

export const FIRST_CONNECTION: LiveConnection = {
  status: "connecting",
  hasBeenLive: false,
};

export function nextLiveConnection(
  state: LiveConnection,
  event: LiveConnectionEvent,
): LiveConnection {
  return event === "opened"
    ? { status: "live", hasBeenLive: true }
    : { status: "lost", hasBeenLive: state.hasBeenLive };
}

let current = FIRST_CONNECTION;
const listeners = new Set<() => void>();

/**
 * Records what the one shared stream just did. Says whether this is the connection coming back after it was lost, so the
 * caller can catch every screen up.
 */
export function reportLiveConnection(event: LiveConnectionEvent): {
  reconnected: boolean;
} {
  const reconnected = event === "opened" && current.status === "lost";
  current = nextLiveConnection(current, event);
  listeners.forEach((listener) => listener());
  return { reconnected };
}

/** Forgets the connection, once nothing is watching it any more. */
export function resetLiveConnection(): void {
  if (current === FIRST_CONNECTION) return;
  current = FIRST_CONNECTION;
  listeners.forEach((listener) => listener());
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

/** The state of the shared live connection right now. */
export function getLiveConnection(): LiveConnection {
  return current;
}

/** The state of the shared live connection, updated as it opens and drops. */
export function useLiveConnection(): LiveConnection {
  return useSyncExternalStore(subscribe, getLiveConnection, getLiveConnection);
}
