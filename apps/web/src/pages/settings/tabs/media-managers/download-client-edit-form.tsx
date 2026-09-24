import { useState } from "react";

import { Field } from "../../../../components/shared/field";
import { quietActionRowClass } from "../../../../components/shared/quiet-section";
import { errorMessage } from "../../../../lib/api/error-message";
import {
  DOWNLOAD_CLIENT_KIND_CREDENTIALS,
  type DownloadClientConnection,
  type DownloadClientConnectionUpdate,
} from "../../../../lib/download-clients/download-clients-api";
import { useUpdateDownloadClientConnection } from "../../../../lib/download-clients/queries";
import {
  mmActionButtonClass,
  mmEditableTextFieldClass,
} from "../../../../lib/ui/mm-control-roles";
import { useLeaveConfirmation, useUnsavedChanges } from "../../unsaved-changes";

const SAVE_FAILURE = "This download client could not be saved.";

type EditForm = {
  name: string;
  base_url: string;
  username: string;
  password: string;
  api_key: string;
};

function formFrom(connection: DownloadClientConnection): EditForm {
  return {
    name: connection.name,
    base_url: connection.base_url,
    username: connection.username ?? "",
    password: "",
    api_key: "",
  };
}

function sameForm(a: EditForm, b: EditForm): boolean {
  return (
    a.name === b.name &&
    a.base_url === b.base_url &&
    a.username === b.username &&
    a.password === b.password &&
    a.api_key === b.api_key
  );
}

/** A blank secret means "leave the saved one alone" — it is only sent when someone typed one. */
function changesFrom(
  form: EditForm,
  initial: EditForm,
): DownloadClientConnectionUpdate {
  const changes: DownloadClientConnectionUpdate = {
    name: form.name.trim(),
    base_url: form.base_url.trim(),
  };
  if (form.username !== initial.username)
    changes.username = form.username.trim();
  if (form.password.trim()) changes.password = form.password.trim();
  if (form.api_key.trim()) changes.api_key = form.api_key.trim();
  return changes;
}

/**
 * Name, address and credentials, edited in place. Nothing is sent until Save; Cancel asks first
 * when anything changed, through the same guard every Settings panel with a Save/Cancel pair uses.
 */
export function DownloadClientEditForm({
  connection,
  onClose,
}: {
  connection: DownloadClientConnection;
  onClose: () => void;
}) {
  const [initial] = useState(() => formFrom(connection));
  const [form, setForm] = useState(initial);
  const update = useUpdateDownloadClientConnection();
  const change = <K extends keyof EditForm>(key: K, value: EditForm[K]) =>
    setForm((current) => ({ ...current, [key]: value }));
  const credentials = DOWNLOAD_CLIENT_KIND_CREDENTIALS[connection.kind];

  const dirty = !sameForm(form, initial);
  useUnsavedChanges(dirty ? connection.name : null);
  const { confirmLeave, dialog } = useLeaveConfirmation();
  const close = () => confirmLeave(dirty ? connection.name : null, onClose);

  const save = () =>
    update.mutate(
      { id: connection.id, data: changesFrom(form, initial) },
      { onSuccess: onClose },
    );

  return (
    <div
      className="mm-quiet-stack mt-3 border-t border-mm-border pt-3"
      data-testid="download-client-edit-form"
    >
      <div className="mm-field-row">
        <Field label="Name" width="medium">
          <input
            data-testid="download-client-edit-name"
            className={mmEditableTextFieldClass}
            value={form.name}
            onChange={(e) => change("name", e.target.value)}
          />
        </Field>
        <Field label="Address" width="wide">
          <input
            data-testid="download-client-edit-base-url"
            className={mmEditableTextFieldClass}
            value={form.base_url}
            onChange={(e) => change("base_url", e.target.value)}
          />
        </Field>
      </div>

      {credentials === "api_key" ? (
        <Field
          label="API key"
          hint="Leave blank to keep the current key."
          width="medium"
        >
          <input
            data-testid="download-client-edit-api-key"
            type="password"
            className={mmEditableTextFieldClass}
            value={form.api_key}
            onChange={(e) => change("api_key", e.target.value)}
          />
        </Field>
      ) : null}

      {credentials === "username_password" ? (
        <div className="mm-field-row">
          <Field label="Username" width="medium">
            <input
              data-testid="download-client-edit-username"
              className={mmEditableTextFieldClass}
              value={form.username}
              onChange={(e) => change("username", e.target.value)}
            />
          </Field>
          <Field
            label="Password"
            hint="Leave blank to keep the current password."
            width="medium"
          >
            <input
              data-testid="download-client-edit-password"
              type="password"
              className={mmEditableTextFieldClass}
              value={form.password}
              onChange={(e) => change("password", e.target.value)}
            />
          </Field>
        </div>
      ) : null}

      {credentials === "password_only" ? (
        <Field
          label="Password"
          hint="Leave blank to keep the current password."
          width="medium"
        >
          <input
            data-testid="download-client-edit-password"
            type="password"
            className={mmEditableTextFieldClass}
            value={form.password}
            onChange={(e) => change("password", e.target.value)}
          />
        </Field>
      ) : null}

      {update.isError ? (
        <p className="mm-status-text--failed text-sm" role="alert">
          {errorMessage(update.error, SAVE_FAILURE)}
        </p>
      ) : null}

      <div className={quietActionRowClass}>
        <button
          type="button"
          data-testid="download-client-edit-save"
          className={mmActionButtonClass({ variant: "primary" })}
          disabled={
            update.isPending || !form.name.trim() || !form.base_url.trim()
          }
          onClick={save}
        >
          {update.isPending ? "Saving…" : "Save"}
        </button>
        <button
          type="button"
          data-testid="download-client-edit-cancel"
          className={mmActionButtonClass({ variant: "secondary" })}
          disabled={update.isPending}
          onClick={close}
        >
          Cancel
        </button>
      </div>
      {dialog}
    </div>
  );
}
