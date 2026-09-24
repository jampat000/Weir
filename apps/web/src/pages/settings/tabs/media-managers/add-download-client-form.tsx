import { useState } from "react";

import { Field } from "../../../../components/shared/field";
import {
  QuietFieldGroup,
  quietActionRowClass,
} from "../../../../components/shared/quiet-section";
import { errorMessage } from "../../../../lib/api/error-message";
import {
  DOWNLOAD_CLIENT_KIND_CREDENTIALS,
  DOWNLOAD_CLIENT_KIND_LABELS,
  type DownloadClientConnection,
  type DownloadClientKind,
} from "../../../../lib/download-clients/download-clients-api";
import { useCreateDownloadClientConnection } from "../../../../lib/download-clients/queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";

const KINDS: DownloadClientKind[] = [
  "sabnzbd",
  "nzbget",
  "qbittorrent",
  "deluge",
  "transmission",
];

type FormState = {
  kind: DownloadClientKind;
  name: string;
  base_url: string;
  username: string;
  password: string;
  api_key: string;
};

const EMPTY_FORM: FormState = {
  kind: "sabnzbd",
  name: "",
  base_url: "",
  username: "",
  password: "",
  api_key: "",
};

export function AddDownloadClientForm({
  onCancel,
  onCreated,
}: {
  onCancel: () => void;
  onCreated: (connection: DownloadClientConnection) => void;
}) {
  const [form, setForm] = useState<FormState>(EMPTY_FORM);
  const create = useCreateDownloadClientConnection();
  const change = <K extends keyof FormState>(key: K, value: FormState[K]) =>
    setForm((current) => ({ ...current, [key]: value }));
  const credentials = DOWNLOAD_CLIENT_KIND_CREDENTIALS[form.kind];
  // SABnzbd's API and Deluge's Web UI always need a credential; the others can run with none.
  const missingRequiredCredential =
    (credentials === "api_key" && !form.api_key.trim()) ||
    (credentials === "password_only" && !form.password.trim());

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        create.mutate(
          {
            kind: form.kind,
            name: form.name,
            base_url: form.base_url,
            username: form.username,
            password: form.password,
            api_key: form.api_key,
            enabled: true,
          },
          { onSuccess: onCreated },
        );
      }}
    >
      <QuietFieldGroup title="Add a download client">
        <div className="mm-quiet-stack">
          <div className="mm-field-row">
            <Field label="Which download client is it?" width="medium">
              <select
                data-testid="download-client-kind"
                className="mm-input"
                value={form.kind}
                onChange={(e) =>
                  change("kind", e.target.value as DownloadClientKind)
                }
              >
                {KINDS.map((kind) => (
                  <option key={kind} value={kind}>
                    {DOWNLOAD_CLIENT_KIND_LABELS[kind]}
                  </option>
                ))}
              </select>
            </Field>
            <Field label="Name" width="medium">
              <input
                data-testid="download-client-name"
                className="mm-input"
                value={form.name}
                placeholder="Living room qBittorrent"
                onChange={(e) => change("name", e.target.value)}
              />
            </Field>
          </div>
          <Field
            label="Where to find it"
            hint="The address you use to open it in a browser."
            width="wide"
          >
            <input
              data-testid="download-client-base-url"
              className="mm-input"
              value={form.base_url}
              placeholder="http://192.0.2.10:8080"
              onChange={(e) => change("base_url", e.target.value)}
            />
          </Field>

          {credentials === "api_key" ? (
            <Field
              label="API key"
              hint="Weir stores this safely and never shows it again."
              width="medium"
            >
              <input
                data-testid="download-client-api-key"
                type="password"
                className="mm-input"
                value={form.api_key}
                onChange={(e) => change("api_key", e.target.value)}
              />
            </Field>
          ) : null}

          {credentials === "username_password" ? (
            <div className="mm-field-row">
              <Field label="Username" width="medium">
                <input
                  data-testid="download-client-username"
                  className="mm-input"
                  value={form.username}
                  onChange={(e) => change("username", e.target.value)}
                />
              </Field>
              <Field
                label="Password"
                hint="Weir stores this safely and never shows it again."
                width="medium"
              >
                <input
                  data-testid="download-client-password"
                  type="password"
                  className="mm-input"
                  value={form.password}
                  onChange={(e) => change("password", e.target.value)}
                />
              </Field>
            </div>
          ) : null}

          {credentials === "password_only" ? (
            <Field
              label="Password"
              hint="Weir stores this safely and never shows it again."
              width="medium"
            >
              <input
                data-testid="download-client-password"
                type="password"
                className="mm-input"
                value={form.password}
                onChange={(e) => change("password", e.target.value)}
              />
            </Field>
          ) : null}
        </div>

        {create.isError ? (
          <p className="mm-status-text--failed mt-2 text-sm" role="alert">
            {errorMessage(create.error, "Could not add this download client.")}
          </p>
        ) : null}

        <div className={quietActionRowClass}>
          <button
            type="submit"
            data-testid="download-client-save"
            className={mmActionButtonClass({ variant: "primary" })}
            disabled={
              create.isPending ||
              !form.name.trim() ||
              !form.base_url.trim() ||
              missingRequiredCredential
            }
          >
            {create.isPending ? "Adding…" : "Add"}
          </button>
          <button
            type="button"
            className={mmActionButtonClass({ variant: "secondary" })}
            onClick={onCancel}
          >
            Cancel
          </button>
        </div>
      </QuietFieldGroup>
    </form>
  );
}
