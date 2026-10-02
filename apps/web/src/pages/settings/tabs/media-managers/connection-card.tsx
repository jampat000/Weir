import { useState } from "react";

import { ConfirmDialog } from "../../../../components/ui/confirm-dialog";
import { errorMessage } from "../../../../lib/api/error-message";
import type { MediaManagerConnection } from "../../../../lib/media-managers/media-managers-api";
import {
  useDeleteMediaManagerConnection,
  useGenerateMediaManagerWebhookSecret,
  useTestMediaManagerConnection,
  useUpdateMediaManagerConnection,
} from "../../../../lib/media-managers/queries";
import { connectionTitle } from "../../../../lib/ui/connection-title";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { ConnectionEditForm } from "./connection-edit-form";
import { ConnectionFolderChain } from "./connection-folder-chain";
import { ConnectionRow } from "./connection-row";
import { ConnectionSetup } from "./connection-setup";
import { ConnectionStatus, FedWorkflows } from "./connection-status";

const TEST_FAILURE = "The media manager could not be tested.";
const TOGGLE_FAILURE = "This media manager could not be turned on or off.";

export function RemoveConnectionDialog({
  connection,
  remove,
  onClose,
}: {
  connection: MediaManagerConnection;
  remove: ReturnType<typeof useDeleteMediaManagerConnection>;
  onClose: () => void;
}) {
  return (
    <ConfirmDialog
      testId="media-manager-remove-confirm"
      title={`Remove ${connectionTitle(connection)}?`}
      description={
        <>
          <p>
            Weir will stop accepting files from {connectionTitle(connection)}.
            Its address, API key and webhook secret go with it, so connecting it
            again means setting it up from scratch.
          </p>
          <p>No media file is touched. This cannot be undone.</p>
        </>
      }
      confirmLabel="Remove media manager"
      tone="danger"
      busy={remove.isPending}
      error={
        remove.isError
          ? errorMessage(remove.error, "Could not remove this media manager.")
          : null
      }
      onCancel={() => {
        remove.reset();
        onClose();
      }}
      onConfirm={() => remove.mutate(connection.id, { onSuccess: onClose })}
    />
  );
}

function toggleLabel(connection: MediaManagerConnection, pending: boolean) {
  if (pending) return connection.enabled ? "Disabling…" : "Enabling…";
  return connection.enabled ? "Disable" : "Enable";
}

/** One connected media manager: whether it answers, what depends on it, and how to wire it up. */
export function ConnectionCard({
  connection,
  fmt,
}: {
  connection: MediaManagerConnection;
  fmt: (iso: string | null) => string;
}) {
  const update = useUpdateMediaManagerConnection();
  const remove = useDeleteMediaManagerConnection();
  const test = useTestMediaManagerConnection();
  const secret = useGenerateMediaManagerWebhookSecret();
  // Remove asks first. Nothing is deleted until the dialog is confirmed.
  const [confirmingRemoval, setConfirmingRemoval] = useState(false);
  const [editing, setEditing] = useState(false);
  const busy =
    update.isPending || remove.isPending || test.isPending || secret.isPending;
  const locked = busy || editing;

  return (
    <ConnectionRow
      title={connectionTitle(connection)}
      status={<ConnectionStatus connection={connection} fmt={fmt} />}
      testId="media-manager-card"
      actions={
        <>
          <button
            type="button"
            data-testid="media-manager-test"
            className={mmActionButtonClass({ variant: "secondary" })}
            disabled={locked}
            onClick={() => test.mutate(connection.id)}
          >
            {test.isPending ? "Testing…" : "Test"}
          </button>
          <button
            type="button"
            data-testid="media-manager-edit"
            className={mmActionButtonClass({ variant: "secondary" })}
            disabled={locked}
            onClick={() => setEditing(true)}
          >
            Edit
          </button>
          <button
            type="button"
            className={mmActionButtonClass({ variant: "secondary" })}
            disabled={locked}
            onClick={() =>
              update.mutate({
                id: connection.id,
                data: { enabled: !connection.enabled },
              })
            }
          >
            {toggleLabel(connection, update.isPending)}
          </button>
          <button
            type="button"
            data-testid="media-manager-remove"
            className={mmActionButtonClass({ variant: "danger-outline" })}
            disabled={locked}
            aria-haspopup="dialog"
            onClick={() => {
              remove.reset();
              setConfirmingRemoval(true);
            }}
          >
            Remove
          </button>
        </>
      }
    >
      <FedWorkflows connection={connection} />
      <ConnectionFolderChain connectionId={connection.id} />

      {test.isError ? (
        <p
          className="mm-status-text mt-2 text-sm"
          data-status="broken"
          role="alert"
        >
          {errorMessage(test.error, TEST_FAILURE)}
        </p>
      ) : null}

      {update.isError ? (
        <p
          className="mm-status-text mt-2 text-sm"
          data-status="broken"
          role="alert"
        >
          {errorMessage(update.error, TOGGLE_FAILURE)}
        </p>
      ) : null}

      {editing ? (
        <ConnectionEditForm
          connection={connection}
          onClose={() => setEditing(false)}
        />
      ) : null}

      {confirmingRemoval ? (
        <RemoveConnectionDialog
          connection={connection}
          remove={remove}
          onClose={() => setConfirmingRemoval(false)}
        />
      ) : null}

      <ConnectionSetup connection={connection} secret={secret} busy={busy} />
    </ConnectionRow>
  );
}
