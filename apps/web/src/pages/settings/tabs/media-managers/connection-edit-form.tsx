import { useState } from "react";

import { Field } from "../../../../components/shared/field";
import { quietActionRowClass } from "../../../../components/shared/quiet-section";
import { errorMessage } from "../../../../lib/api/error-message";
import {
  MEDIA_MANAGER_KIND_LABELS,
  type MediaManagerConnection,
  type MediaManagerConnectionUpdate,
} from "../../../../lib/media-managers/media-managers-api";
import { useUpdateMediaManagerConnection } from "../../../../lib/media-managers/queries";
import {
  mmActionButtonClass,
  mmCheckboxControlClass,
  mmEditableTextFieldClass,
} from "../../../../lib/ui/mm-control-roles";
import { useLeaveConfirmation, useUnsavedChanges } from "../../unsaved-changes";

const SAVE_FAILURE = "This media manager could not be saved.";

/** Only Radarr and Sonarr run the remote-path-mapping flow the Downloaded Scan command helps with; Deluno always uses its own hand-off. */
const DOWNLOADED_SCAN_KINDS = new Set<MediaManagerConnection["kind"]>([
  "radarr",
  "sonarr",
]);

type EditForm = {
  name: string;
  base_url: string;
  api_key: string;
  downloaded_scan_enabled: boolean;
};

function formFrom(connection: MediaManagerConnection): EditForm {
  return {
    name: connection.name,
    base_url: connection.base_url,
    api_key: "",
    downloaded_scan_enabled: connection.downloaded_scan_enabled,
  };
}

function sameForm(a: EditForm, b: EditForm): boolean {
  return (
    a.name === b.name &&
    a.base_url === b.base_url &&
    a.api_key === b.api_key &&
    a.downloaded_scan_enabled === b.downloaded_scan_enabled
  );
}

/**
 * A blank API key means "leave the saved one alone" — it is only sent when someone typed one.
 * `downloaded_scan_enabled` is only sent for a kind that offers it; a Deluno connection never shows
 * the toggle, so its own hand-off setup is never touched by editing name or address.
 */
function changesFrom(
  form: EditForm,
  kind: MediaManagerConnection["kind"],
): MediaManagerConnectionUpdate {
  const changes: MediaManagerConnectionUpdate = {
    name: form.name.trim(),
    base_url: form.base_url.trim(),
  };
  if (form.api_key.trim()) changes.api_key = form.api_key.trim();
  if (DOWNLOADED_SCAN_KINDS.has(kind)) {
    changes.downloaded_scan_enabled = form.downloaded_scan_enabled;
  }

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
      { id: connection.id, data: changesFrom(form, connection.kind) },
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

      {DOWNLOADED_SCAN_KINDS.has(connection.kind) ? (
        <label className="mm-library-toggle">
          <input
            data-testid="media-manager-edit-downloaded-scan"
            type="checkbox"
            className={mmCheckboxControlClass}
            checked={form.downloaded_scan_enabled}
            onChange={(e) =>
              change("downloaded_scan_enabled", e.target.checked)
            }
          />
          <span>
            <span className="mm-library-toggle__label">
              Scan for downloaded files after cleaning
            </span>
            <span className="mm-library-toggle__hint">
              After Weir cleans a file, ask{" "}
              {MEDIA_MANAGER_KIND_LABELS[connection.kind]} to import it with its
              Downloaded Scan command. Off by default.
            </span>
          </span>
        </label>
      ) : null}

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
