import { useState } from "react";

import { Field } from "../../../../components/shared/field";
import { quietActionRowClass } from "../../../../components/shared/quiet-section";
import { errorMessage } from "../../../../lib/api/error-message";
import {
  MEDIA_MANAGER_KIND_LABELS,
  OPTIONAL_ADDRESS_HINT,
  connectionNeedsAddress,
  type MediaManagerConnection,
  type MediaManagerConnectionUpdate,
} from "../../../../lib/media-managers/media-managers-api";
import { useUpdateMediaManagerConnection } from "../../../../lib/media-managers/queries";
import { connectionTitle } from "../../../../lib/ui/connection-title";
import {
  mmActionButtonClass,
  mmCheckboxControlClass,
  mmEditableTextFieldClass,
} from "../../../../lib/ui/mm-control-roles";
import { useLeaveConfirmation, useUnsavedChanges } from "../../unsaved-changes";
import { ConnectionNicknameField } from "./connection-nickname-field";

const SAVE_FAILURE = "This media manager could not be saved.";

/** Only Radarr and Sonarr run the remote-path-mapping flow the Downloaded Scan command helps with; Deluno always uses its own hand-off. */
const DOWNLOADED_SCAN_KINDS = new Set<MediaManagerConnection["kind"]>([
  "radarr",
  "sonarr",
]);

type EditForm = {
  base_url: string;
  api_key: string;
  nickname: string;
  downloaded_scan_enabled: boolean;
};

function formFrom(connection: MediaManagerConnection): EditForm {
  return {
    base_url: connection.base_url,
    api_key: "",
    nickname: connection.nickname ?? "",
    downloaded_scan_enabled: connection.downloaded_scan_enabled,
  };
}

function sameForm(a: EditForm, b: EditForm): boolean {
  return (
    a.base_url === b.base_url &&
    a.api_key === b.api_key &&
    a.nickname === b.nickname &&
    a.downloaded_scan_enabled === b.downloaded_scan_enabled
  );
}

/**
 * A blank API key means "leave the saved one alone" — it is only sent when someone typed one. The nickname is only sent
 * when it changed, and a blank one clears it.
 * `downloaded_scan_enabled` is only sent for a kind that offers it; a Deluno connection never shows
 * the toggle, so its own hand-off setup is never touched by editing its address.
 */
function changesFrom(
  form: EditForm,
  initial: EditForm,
  kind: MediaManagerConnection["kind"],
): MediaManagerConnectionUpdate {
  const changes: MediaManagerConnectionUpdate = {
    base_url: form.base_url.trim(),
  };
  if (form.api_key.trim()) changes.api_key = form.api_key.trim();
  if (form.nickname !== initial.nickname)
    changes.nickname = form.nickname.trim();
  if (DOWNLOADED_SCAN_KINDS.has(kind)) {
    changes.downloaded_scan_enabled = form.downloaded_scan_enabled;
  }

  return changes;
}

/**
 * Address and API key, edited in place. Nothing is sent until Save; Cancel asks first when
 * anything changed, through the same guard every setup panel with a Save/Cancel pair uses.
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

  const needsAddress = connectionNeedsAddress(connection.kind);
  const dirty = !sameForm(form, initial);
  useUnsavedChanges(dirty ? connectionTitle(connection) : null);
  const { confirmLeave, dialog } = useLeaveConfirmation();
  const close = () =>
    confirmLeave(dirty ? connectionTitle(connection) : null, onClose);

  const save = () =>
    update.mutate(
      { id: connection.id, data: changesFrom(form, initial, connection.kind) },
      { onSuccess: onClose },
    );

  return (
    <div
      className="mm-quiet-stack mt-3 border-t border-mm-border pt-3"
      data-testid="media-manager-edit-form"
    >
      <div className="mm-field-row">
        <Field
          label="Address"
          hint={needsAddress ? undefined : OPTIONAL_ADDRESS_HINT}
          width="wide"
        >
          <input
            data-testid="media-manager-edit-base-url"
            autoComplete="url"
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
          autoComplete="new-password"
          className={mmEditableTextFieldClass}
          value={form.api_key}
          onChange={(e) => change("api_key", e.target.value)}
        />
      </Field>

      <ConnectionNicknameField
        testId="media-manager-edit-nickname"
        className={mmEditableTextFieldClass}
        value={form.nickname}
        onChange={(value) => change("nickname", value)}
      />

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
        <p className="mm-status-text text-sm" data-status="broken" role="alert">
          {errorMessage(update.error, SAVE_FAILURE)}
        </p>
      ) : null}

      <div className={quietActionRowClass}>
        <button
          type="button"
          data-testid="media-manager-edit-save"
          className={mmActionButtonClass({ variant: "primary" })}
          disabled={update.isPending || (needsAddress && !form.base_url.trim())}
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
