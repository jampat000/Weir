import { useEffect } from "react";

import { errorMessage } from "../../../../lib/api/error-message";
import {
  useApplyUpdateMutation,
  useUpdateStateQuery,
} from "../../../../lib/settings/queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";

/** How long to let the tray actually start restarting before the first readiness check. */
const RESTART_GRACE_MS = 2000;
/** How often to check `/ready` while Weir is restarting. */
const READY_POLL_MS = 1000;

type ReadyPayload = { ready?: boolean };

/**
 * Once the restart signal is sent, the tray kills and relaunches the server process: this browser tab
 * has no other way to learn when that finishes. Polling `/ready` and reloading is what turns "Restart
 * signal sent" into the page actually coming back on its own (#703).
 */
function useReloadWhenReady(active: boolean): void {
  useEffect(() => {
    if (!active) return undefined;
    let cancelled = false;
    let timer: number | undefined;

    const poll = async () => {
      try {
        const response = await fetch("/ready", { cache: "no-store" });
        const body = (await response
          .json()
          .catch(() => null)) as ReadyPayload | null;
        if (!cancelled && body?.ready) {
          window.location.reload();
          return;
        }
      } catch {
        // The server is mid-restart and not answering yet; keep waiting.
      }
      if (!cancelled) {
        timer = window.setTimeout(() => void poll(), READY_POLL_MS);
      }
    };

    timer = window.setTimeout(() => void poll(), RESTART_GRACE_MS);
    return () => {
      cancelled = true;
      if (timer !== undefined) window.clearTimeout(timer);
    };
  }, [active]);
}

/** On Windows, a downloaded update waits for a restart; this says so and offers it. */
export function UpdateReadyNotice() {
  const updateStateQ = useUpdateStateQuery(true);
  const applyUpdate = useApplyUpdateMutation();
  const state = updateStateQ.data;
  useReloadWhenReady(applyUpdate.isSuccess);
  if (!state?.downloaded) return null;

  return (
    <div className="mm-update-ready">
      <div className="min-w-0">
        <p className="mm-status-text--healthy text-sm font-semibold">
          Update ready to install
          {state.pending_version ? ` — v${state.pending_version}` : ""}
        </p>
        <p className="mt-0.5 text-xs text-mm-text3">
          The update has been downloaded. Restart Weir to apply it.
        </p>
        {applyUpdate.isError ? (
          <p className="mm-status-text--failed mt-1 text-xs" role="alert">
            {errorMessage(applyUpdate.error, "Could not signal restart.")}
          </p>
        ) : null}
        {applyUpdate.isSuccess ? (
          <p className="mm-status-text--healthy mt-1 text-xs" role="status">
            Weir is restarting to finish the update. This page will reload by
            itself.
          </p>
        ) : null}
      </div>
      <button
        type="button"
        className={mmActionButtonClass({ variant: "primary" })}
        disabled={applyUpdate.isPending || applyUpdate.isSuccess}
        onClick={() => applyUpdate.mutate()}
      >
        {applyUpdate.isPending ? "Restarting..." : "Restart to apply"}
      </button>
    </div>
  );
}
