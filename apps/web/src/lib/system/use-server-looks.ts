import { useSyncExternalStore } from "react";

import {
  getServerLooks,
  subscribeSystemChecks,
} from "../activity/use-activity-stream-invalidation";
import type { ServerLooks } from "./system-checks-frame";

const subscribe = (onChange: () => void): (() => void) =>
  subscribeSystemChecks(onChange);

/**
 * When the server last checked the workflows' folders and whether Weir is ready, as its `system.checks` frames say: the
 * newest times it has sent, kept for a card that opens between two frames.
 */
export function useServerLooks(): ServerLooks {
  return useSyncExternalStore(subscribe, getServerLooks);
}
