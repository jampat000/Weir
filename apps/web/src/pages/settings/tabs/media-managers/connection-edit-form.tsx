import { useState } from "react";

import { Field } from "../../../../components/shared/field";
import { quietActionRowClass } from "../../../../components/shared/quiet-section";
import { errorMessage } from "../../../../lib/api/error-message";
import type {
  MediaManagerConnection,
  MediaManagerConnectionUpdate,
} from "../../../../lib/media-managers/media-managers-api";
import { useUpdateMediaManagerConnection } from "../../../../lib/media-managers/queries";
import {
  mmActionButtonClass,
  mmEditableTextFieldClass,
} from "../../../../lib/ui/mm-control-roles";
import { useLeaveConfirmation, useUnsavedChanges } from "../../unsaved-changes";

const SAVE_FAILURE = "This media manager could not be saved.";

type EditForm = {
  name: string;
  base_url: string;
  api_key: string;
};

function formFrom(connection: MediaManagerConnection): EditForm {
  return { name: connection.name, base_url: connection.base_url, api_key: "" };
}

function sameForm(a: EditForm, b: EditForm): boolean {
  return (
    a.name === b.name && a.base_url === b.base_url && a.api_key === b.api_key
  );
}

/** A blank API key means "leave the saved one alone" — it is only sent when someone typed one. */
function changesFrom(form: EditForm): MediaManagerConnectionUpdate {
  const changes: MediaManagerConnectionUpdate = {
    name: form.name.trim(),
    base_url: form.base_url.trim(),
  };
  if (form.api_key.trim()) changes.api_key = form.api_key.trim();
  return changes;
}

/**
 * Name, address and API key, edited in place. Nothing is sent until Save; Cancel asks first when
 * anything changed, through the same guard every Settings panel with a Save/Cancel pair uses.
 */
export function ConnectionEditForm({
  connection,
  onClose,
}: {
  connection: MediaManagerConnection;
  onClose: () => void;
}) {
  const [initial] = useState(() => formFrom(connection));
  const [form, setForm] = useState(initial);
  const update = useUpdateMediaManagerConnection();
  const change = <K extends keyof EditForm>(key: K, value: EditForm[K]) =>
    setForm((current) => ({ ...current, [key]: value }));

  const dirty = !sameForm(form, initial);
  useUnsavedChanges(dirty ? connection.name : null);
  const { confirmLeave, dialog } = useLeaveConfirmation();
  const close = () => confirmLeave(dirty ? connection.name : null, onClose);

  const save = () =>
    update.mutate(
      { id: connection.id, data: changesFrom(form) },
      { onSuccess: onClose },
    );

  return (
    <div
      className="mm-quiet-stack mt-3 border-t border-mm-border pt-3"
      data-testid="media-manager-edit-form"
    >
      <div className="mm-field-row">
        <Field label="Name" width="medium">
          <input
            data-testid="media-manager-edit-name"
            className={mmEditableTextFieldClass}
            value={form.name}
            onChange={(e) => change("name", e.target.value)}
          />
        </Field>
        <Field label="Address" width="wide">
          <input
            data-testid="media-manager-edit-base-url"
            className={mmEditableTextFieldClass}
            value={form.base_url}
            onChange={(e) => change("base_url", e.target.value)}
          />
        </Field>
      </div>
      <Field
        label="API key"
        hint="Leave blank to keep the current key."
        width="medium"
      >
        <input
          data-testid="media-manager-edit-api-key"
          type="password"
          className={mmEditableTextFieldClass}
          value={form.api_key}
          onChange={(e) => change("api_key", e.target.value)}
        />
      </Field>

      {update.isError ? (
        <p className="mm-status-text--failed text-sm" role="alert">
          {errorMessage(update.error, SAVE_FAILURE)}
        </p>
      ) : null}

      <div className={quietActionRowClass}>
        <button
          type="button"
          data-testid="media-manager-edit-save"
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
          data-testid="media-manager-edit-cancel"
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
