import { useEffect } from "react";
import { useQueryClient } from "@tanstack/react-query";

import {
  invalidateLive,
  subscribeLiveSignals,
} from "../activity/use-activity-stream-invalidation";
import { authKeys } from "../auth/query-keys";
import { bundleHasChanged } from "./bundle-changed";
import { LIVE_TOPIC_QUERIES } from "./live-topics";

/**
 * Keeps every screen current from what the shared stream says, with no reload: a `data.changed` frame reads again the
 * queries of its topic, a connection that comes back after being lost reads every query again, a server that restarted
 * reloads the page when it now serves a newer build, and a stream the server refuses asks whether the session has ended.
 * Mounted once, by the shell.
 */
export function useLiveSync(): void {
  const qc = useQueryClient();

  useEffect(
    () =>
      subscribeLiveSignals((signal) => {
        switch (signal.type) {
          case "changed":
            LIVE_TOPIC_QUERIES[signal.topic].forEach((queryKey) =>
              invalidateLive(qc, { queryKey }),
            );
            break;
          case "reconnected":
            invalidateLive(qc, { queryKey: [] });
            break;
          case "restarted":
            void bundleHasChanged().then((changed) => {
              if (changed) window.location.reload();
            });
            break;
          case "refused":
            invalidateLive(qc, { queryKey: authKeys.me });
            break;
        }
      }),
    [qc],
  );
}
