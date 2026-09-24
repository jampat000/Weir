import { useState } from "react";

import { Field } from "../../../../components/shared/field";
import {
  QuietFieldGroup,
  quietActionRowClass,
} from "../../../../components/shared/quiet-section";
import { errorMessage } from "../../../../lib/api/error-message";
import {
  MEDIA_MANAGER_KIND_LABELS,
  type MediaManagerKind,
} from "../../../../lib/media-managers/media-managers-api";
import { useCreateMediaManagerConnection } from "../../../../lib/media-managers/queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";

const KINDS: MediaManagerKind[] = ["radarr", "sonarr", "deluno", "native"];

/** What choosing each one means, without naming what Weir does internally. */
const KIND_BLURBS: Record<MediaManagerKind, string> = {
  radarr:
    "Lets Weir check what Radarr is still importing, and download a film again.",
  sonarr:
    "Lets Weir check what Sonarr is still importing, and download an episode again.",
  deluno: "Hands a file to Weir to work on, and waits to be told it is ready.",
  native: "Anything else that can send Weir a message.",
};

type FormState = {
  kind: MediaManagerKind;
  name: string;
  base_url: string;
  api_key: string;
};

const EMPTY_FORM: FormState = {
  kind: "deluno",
  name: "",
  base_url: "",
  api_key: "",
};

export function AddConnectionForm({ onCancel }: { onCancel: () => void }) {
  const [form, setForm] = useState<FormState>(EMPTY_FORM);
  const create = useCreateMediaManagerConnection();
  const change = <K extends keyof FormState>(key: K, value: FormState[K]) =>
    setForm((current) => ({ ...current, [key]: value }));

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        create.mutate({ ...form, enabled: true }, { onSuccess: onCancel });
      }}
    >
      <QuietFieldGroup title="Add an app">
        <div className="mm-quiet-stack">
          <div className="mm-field-row">
            <Field
              label="Which app is it?"
              hint={KIND_BLURBS[form.kind]}
              width="medium"
            >
              <select
                data-testid="media-manager-kind"
                className="mm-input"
                value={form.kind}
                onChange={(e) =>
                  change("kind", e.target.value as MediaManagerKind)
                }
              >
                {KINDS.map((kind) => (
                  <option key={kind} value={kind}>
                    {MEDIA_MANAGER_KIND_LABELS[kind]}
                  </option>
                ))}
              </select>
            </Field>
            <Field label="Name" width="medium">
              <input
                data-testid="media-manager-name"
                className="mm-input"
                value={form.name}
                placeholder="Deluno"
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
              data-testid="media-manager-base-url"
              className="mm-input"
              value={form.base_url}
              placeholder="http://192.0.2.10:5099"
              onChange={(e) => change("base_url", e.target.value)}
            />
          </Field>
          <Field
            label="API key"
            hint="Weir stores this safely and never shows it again."
            width="medium"
          >
            <input
              data-testid="media-manager-api-key"
              type="password"
              className="mm-input"
              value={form.api_key}
              onChange={(e) => change("api_key", e.target.value)}
            />
          </Field>
        </div>

        {create.isError ? (
          <p className="mm-status-text--failed mt-2 text-sm" role="alert">
            {errorMessage(create.error, "Could not add this media manager.")}
          </p>
        ) : null}

        <div className={quietActionRowClass}>
          <button
            type="submit"
            data-testid="media-manager-save"
            className={mmActionButtonClass({ variant: "primary" })}
            disabled={create.isPending || !form.name.trim()}
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
