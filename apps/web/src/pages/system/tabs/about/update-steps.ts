import type {
  UpdateStateOut,
  UpdateStatus,
} from "../../../../lib/settings/types";

/** One of the three update buttons: its words and whether it applies right now. */
export type UpdateButton = { label: string; enabled: boolean };

export type UpdateButtons = {
  check: UpdateButton;
  download: UpdateButton;
  apply: UpdateButton;
};

/** What the page itself has under way, which the tray's state has not caught up with yet. */
export type UpdateAsks = {
  /** A check or a download has been asked for and the answer has not come back. */
  asking: boolean;
  /** The restart has been asked for; the server is about to stop. */
  restarting: boolean;
};

/**
 * Which buttons apply, following the tray menu's update item: Check for updates, then Checking, then Download update, then
 * Downloading, then Restart to update. Nothing applies while a step is under way or before the tray's state is known, and
 * once the update is downloaded only the restart does. The download is offered for any update Weir knows of, found by the
 * tray or by Weir's own look at the releases: the tray finds it first when it has not looked yet.
 */
export function updateButtons(
  state: UpdateStateOut | undefined,
  status: UpdateStatus,
  { asking, restarting }: UpdateAsks,
): UpdateButtons {
  const step = state?.state;
  const downloaded = state?.downloaded === true;
  const busy =
    state === undefined ||
    asking ||
    restarting ||
    step === "checking" ||
    step === "downloading";
  const updateKnown =
    Boolean(state?.pending_version) ||
    status.status === "update_available" ||
    status.known_update_available === true;
  return {
    check: {
      label: step === "checking" ? "Checking…" : "Check now",
      enabled: !busy && !downloaded,
    },
    download: {
      label: step === "downloading" ? "Downloading update…" : "Download update",
      enabled: !busy && !downloaded && updateKnown,
    },
    apply: {
      label: restarting ? "Restarting…" : "Restart and apply",
      enabled: !busy && downloaded,
    },
  };
}
