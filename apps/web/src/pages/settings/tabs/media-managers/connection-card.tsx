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
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { ConnectionEditForm } from "./connection-edit-form";
import { ConnectionFolderChain } from "./connection-folder-chain";
import { ConnectionSetup } from "./connection-setup";
import {
  ConnectionStatusPanel,
  LinkedLibraries,
  UnsignedWebhookWarning,
} from "./connection-status";

const TEST_FAILURE = "The media manager could not be tested.";
const TOGGLE_FAILURE = "This media manager could not be turned on or off.";

function RemoveConnectionDialog({
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
      title={`Remove ${connection.name}?`}
      description={
        <>
          <p>
            Weir will stop accepting files from {connection.name}. Its address,
            API key and webhook secret go with it, so connecting it again means
            setting it up from scratch.
          </p>
          <p>No media file is touched. This cannot be undone.</p>
        </>
      }
      confirmLabel="Remove media manager"
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
    <section className="mm-quiet-section" data-testid="media-manager-card">
      <div className="mm-quiet-section__head">
        <h3 className="mm-quiet-section__title">{connection.name}</h3>
        <div className="mm-quiet-section__aside">
          <span
            className={`mm-quiet-badge${connection.enabled ? "" : " mm-quiet-badge--off"}`}
          >
            {connection.enabled ? "Enabled" : "Disabled"}
          </span>
        </div>
      </div>
      <div className="mm-quiet-section__body">
        <ConnectionStatusPanel connection={connection} fmt={fmt} />
        <UnsignedWebhookWarning connection={connection} />
        <LinkedLibraries connectionId={connection.id} />

        <div className="mt-3 flex flex-wrap gap-2">
          <button
            type="button"
            data-testid="media-manager-test"
            className={mmActionButtonClass({ variant: "primary" })}
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
            className={mmActionButtonClass({ variant: "tertiary" })}
            disabled={locked}
            aria-haspopup="dialog"
            onClick={() => {
              remove.reset();
              setConfirmingRemoval(true);
            }}
          >
            Remove
          </button>
        </div>

        {test.isError ? (
          <p className="mm-status-text--failed mt-2 text-sm" role="alert">
            {errorMessage(test.error, TEST_FAILURE)}
          </p>
        ) : null}

        {update.isError ? (
          <p className="mm-status-text--failed mt-2 text-sm" role="alert">
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
        <ConnectionFolderChain connectionId={connection.id} />
      </div>
    </section>
  );
}
