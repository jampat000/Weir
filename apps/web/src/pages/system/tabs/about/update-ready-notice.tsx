import { useEffect } from "react";

import { subscribeLiveSignals } from "../../../../lib/activity/use-activity-stream-invalidation";
import { errorMessage } from "../../../../lib/api/error-message";
import {
  useApplyUpdateMutation,
  useUpdateStateQuery,
} from "../../../../lib/settings/queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";

/**
 * Once the restart signal is sent, the tray stops and starts the server. The page needs no check of its own to come back:
 * the shared stream reconnects and hears a new boot id, which reloads every query and, when the server now serves a newer
 * build, the page itself (`useLiveSync`). If the server came back and the update is still waiting, the notice goes back to
 * offering the restart rather than saying one is under way for ever.
 */
function useForgetRestartWhenServerRestarted(forget: () => void): void {
  useEffect(
    () =>
      subscribeLiveSignals((signal) => {
        if (signal.type === "restarted") forget();
      }),
    [forget],
  );
}

/** On Windows, a downloaded update waits for a restart; this says so and offers it. */
export function UpdateReadyNotice() {
  const updateStateQ = useUpdateStateQuery(true);
  const applyUpdate = useApplyUpdateMutation();
  const state = updateStateQ.data;
  useForgetRestartWhenServerRestarted(applyUpdate.reset);
  if (!state?.downloaded) return null;

  return (
    <div className="mm-update-ready" data-status="todo">
      <div className="min-w-0">
        <p className="mm-status-text text-sm font-semibold">
          Update ready to install
          {state.pending_version ? ` — v${state.pending_version}` : ""}
        </p>
        <p className="mt-0.5 text-xs text-mm-text3">
          The update has been downloaded. Restart Weir to apply it.
        </p>
        {applyUpdate.isError ? (
          <p
            className="mm-status-text mt-1 text-xs"
            data-status="broken"
            role="alert"
          >
            {errorMessage(applyUpdate.error, "Could not signal restart.")}
          </p>
        ) : null}
        {applyUpdate.isSuccess ? (
          <p
            className="mm-status-text mt-1 text-xs"
            data-status="doing"
            role="status"
          >
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
