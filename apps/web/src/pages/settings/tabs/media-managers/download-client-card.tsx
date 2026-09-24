import { useState } from "react";

import { ConfirmDialog } from "../../../../components/ui/confirm-dialog";
import { errorMessage } from "../../../../lib/api/error-message";
import {
  DOWNLOAD_CLIENT_KIND_LABELS,
  type DownloadClientConnection,
} from "../../../../lib/download-clients/download-clients-api";
import {
  useDeleteDownloadClientConnection,
  useTestDownloadClientConnection,
  useUpdateDownloadClientConnection,
} from "../../../../lib/download-clients/queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { DownloadClientEditForm } from "./download-client-edit-form";

const TEST_FAILURE = "The download client could not be tested.";
const TOGGLE_FAILURE = "This download client could not be turned on or off.";

type Formatter = (iso: string | null) => string;

/** A result only counts with a time behind it, the same rule the media-manager card uses. */
function lastResult(connection: DownloadClientConnection): boolean | null {
  return connection.last_test_at ? (connection.last_test_ok ?? null) : null;
}

function headline(connection: DownloadClientConnection): string {
  const result = lastResult(connection);
  if (result === null) return connection.enabled ? "Not tested yet" : "Off";
  return result ? "Answering" : "Not answering";
}

function tone(result: boolean | null): string {
  if (result === null) return "text-mm-text";
  return result ? "mm-status-text--healthy" : "mm-status-text--failed";
}

function DownloadClientStatus({
  connection,
  fmt,
}: {
  connection: DownloadClientConnection;
  fmt: Formatter;
}) {
  const result = lastResult(connection);
  return (
    <div
      className="mt-3 text-sm text-mm-text2"
      data-testid="download-client-status"
    >
      <p className={`text-sm font-medium ${tone(result)}`}>
        {headline(connection)}
      </p>
      <p className="mt-1 text-xs text-mm-text2">
        Last checked:{" "}
        <span className="font-medium text-mm-text">
          {connection.last_test_at ? fmt(connection.last_test_at) : "never"}
        </span>
      </p>
      {result === false && connection.last_test_detail ? (
        <p className="mm-status-text--failed mt-1 text-xs">
          {connection.last_test_detail}
        </p>
      ) : null}
    </div>
  );
}

function RemoveDownloadClientDialog({
  connection,
  remove,
  onClose,
}: {
  connection: DownloadClientConnection;
  remove: ReturnType<typeof useDeleteDownloadClientConnection>;
  onClose: () => void;
}) {
  return (
    <ConfirmDialog
      testId="download-client-remove-confirm"
      title={`Remove ${connection.name}?`}
      description={
        <>
          <p>
            Weir will stop reading folder suggestions from {connection.name}.
            Its address and saved credentials go with it, so connecting it again
            means setting it up from scratch.
          </p>
          <p>
            Weir never controlled {connection.name} — nothing about it changes.
            No watched folder set from its suggestion is undone.
          </p>
        </>
      }
      confirmLabel="Remove download client"
      busy={remove.isPending}
      error={
        remove.isError
          ? errorMessage(remove.error, "Could not remove this download client.")
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

function toggleLabel(connection: DownloadClientConnection, pending: boolean) {
  if (pending) return connection.enabled ? "Disabling…" : "Enabling…";
  return connection.enabled ? "Disable" : "Enable";
}

/** One connected download client: whether it answers, and how to wire it up or remove it. */
export function DownloadClientCard({
  connection,
  fmt,
}: {
  connection: DownloadClientConnection;
  fmt: Formatter;
}) {
  const update = useUpdateDownloadClientConnection();
  const remove = useDeleteDownloadClientConnection();
  const test = useTestDownloadClientConnection();
  const [confirmingRemoval, setConfirmingRemoval] = useState(false);
  const [editing, setEditing] = useState(false);
  const busy = update.isPending || remove.isPending || test.isPending;
  const locked = busy || editing;

  return (
    <section className="mm-quiet-section" data-testid="download-client-card">
      <div className="mm-quiet-section__head">
        <h3 className="mm-quiet-section__title">{connection.name}</h3>
        <div className="mm-quiet-section__aside">
          <span className="mm-quiet-badge">
            {DOWNLOAD_CLIENT_KIND_LABELS[connection.kind]}
          </span>
          <span
            className={`mm-quiet-badge${connection.enabled ? "" : " mm-quiet-badge--off"}`}
          >
            {connection.enabled ? "Enabled" : "Disabled"}
          </span>
        </div>
      </div>
      <div className="mm-quiet-section__body">
        <DownloadClientStatus connection={connection} fmt={fmt} />

        <div className="mt-3 flex flex-wrap gap-2">
          <button
            type="button"
            data-testid="download-client-test"
            className={mmActionButtonClass({ variant: "primary" })}
            disabled={locked}
            onClick={() => test.mutate(connection.id)}
          >
            {test.isPending ? "Testing…" : "Test"}
          </button>
          <button
            type="button"
            data-testid="download-client-edit"
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
            data-testid="download-client-remove"
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
          <DownloadClientEditForm
            connection={connection}
            onClose={() => setEditing(false)}
          />
        ) : null}

        {confirmingRemoval ? (
          <RemoveDownloadClientDialog
            connection={connection}
            remove={remove}
            onClose={() => setConfirmingRemoval(false)}
          />
        ) : null}
      </div>
    </section>
  );
}
