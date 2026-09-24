import { errorMessage } from "../../../../lib/api/error-message";
import {
  useApplyUpdateMutation,
  useUpdateStateQuery,
} from "../../../../lib/settings/queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";

/** On Windows, a downloaded update waits for a restart; this says so and offers it. */
export function UpdateReadyNotice() {
  const updateStateQ = useUpdateStateQuery(true);
  const applyUpdate = useApplyUpdateMutation();
  const state = updateStateQ.data;
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
          <p className="mm-status-text--healthy mt-1 text-xs">
            Restart signal sent — the tray will apply the update shortly.
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
