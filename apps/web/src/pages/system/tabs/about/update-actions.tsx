import { useEffect } from "react";

import { subscribeLiveSignals } from "../../../../lib/activity/use-activity-stream-invalidation";
import { errorMessage } from "../../../../lib/api/error-message";
import {
  useApplyUpdateMutation,
  useCheckUpdateMutation,
  useDownloadUpdateMutation,
  useUpdateStateQuery,
} from "../../../../lib/settings/queries";
import type {
  UpdateStateOut,
  UpdateStatus,
} from "../../../../lib/settings/types";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { type UpdateButton, updateButtons } from "./update-steps";

/**
 * Once the restart signal is sent, the tray stops and starts the server. The page needs no check of its own to come back:
 * the shared stream reconnects and hears a new boot id, which reloads every query and, when the server now serves a newer
 * build, the page itself (`useLiveSync`). If the server came back and the update is still waiting, the buttons go back to
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

function versionSuffix(state: UpdateStateOut): string {
  return state.pending_version ? ` — v${state.pending_version}` : "";
}

/** What the tray is doing about the update, when that is more than the buttons say. */
function UpdateNotice({
  state,
  restartSent,
}: {
  state: UpdateStateOut;
  restartSent: boolean;
}) {
  if (state.downloaded) {
    return (
      <div className="mm-update-ready" data-status="todo">
        <div className="min-w-0">
          <p className="mm-status-text text-sm font-semibold">
            Update ready to install{versionSuffix(state)}
          </p>
          <p className="mt-0.5 text-xs text-mm-text3">
            The update has been downloaded. Restart Weir to apply it.
          </p>
          {restartSent ? (
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
      </div>
    );
  }
  if (state.state === "downloading") {
    return (
      <div className="mm-update-ready" data-status="doing" role="status">
        <p className="mm-status-text text-sm font-semibold">
          Downloading the update{versionSuffix(state)}
        </p>
      </div>
    );
  }
  if (state.state === "failed" && state.failure) {
    return (
      <div className="mm-update-ready" data-status="broken" role="alert">
        <p className="mm-status-text text-sm font-semibold">{state.failure}</p>
      </div>
    );
  }
  return null;
}

/**
 * On Windows, the three steps of an update, each asked of the tray the moment it is pressed: Check now, Download update,
 * and Restart and apply. The tray's answers come back through the stream, so the buttons and the notice follow it live.
 */
export function UpdateActions({ status }: { status: UpdateStatus }) {
  const state = useUpdateStateQuery(true).data;
  const check = useCheckUpdateMutation();
  const download = useDownloadUpdateMutation();
  const apply = useApplyUpdateMutation();
  useForgetRestartWhenServerRestarted(apply.reset);

  const restartSent = apply.isSuccess;
  const buttons = updateButtons(state, status, {
    asking: check.isPending || download.isPending,
    restarting: apply.isPending || restartSent,
  });
  const failed = [check, download, apply].find((step) => step.isError);

  const ask = (step: { mutate: () => void }) => {
    check.reset();
    download.reset();
    step.mutate();
  };
  const buttonClass = (button: UpdateButton) =>
    `${mmActionButtonClass({ variant: button.primary ? "primary" : "secondary" })} mm-sys-btn`;

  return (
    <div className="mt-3" data-testid="suite-settings-update-actions">
      {state ? <UpdateNotice state={state} restartSent={restartSent} /> : null}
      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          className={buttonClass(buttons.check)}
          disabled={!buttons.check.enabled}
          onClick={() => ask(check)}
        >
          {buttons.check.label}
        </button>
        <button
          type="button"
          className={buttonClass(buttons.download)}
          disabled={!buttons.download.enabled}
          onClick={() => ask(download)}
        >
          {buttons.download.label}
        </button>
        <button
          type="button"
          className={buttonClass(buttons.apply)}
          disabled={!buttons.apply.enabled}
          onClick={() => apply.mutate()}
        >
          {buttons.apply.label}
        </button>
      </div>
      {failed ? (
        <p
          className="mm-status-text mt-2 text-xs"
          data-status="broken"
          role="alert"
        >
          {errorMessage(failed.error, "Could not ask Weir to do that.")}
        </p>
      ) : null}
    </div>
  );
}
