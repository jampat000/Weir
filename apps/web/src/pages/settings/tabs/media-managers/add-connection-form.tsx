import { useState, type RefObject } from "react";

import { Field } from "../../../../components/shared/field";
import {
  QuietSection,
  quietActionRowClass,
} from "../../../../components/shared/quiet-section";
import { errorMessage } from "../../../../lib/api/error-message";
import {
  MEDIA_MANAGER_KIND_LABELS,
  OPTIONAL_ADDRESS_HINT,
  connectionNeedsAddress,
  type MediaManagerConnection,
  type MediaManagerKind,
} from "../../../../lib/media-managers/media-managers-api";
import { useCreateMediaManagerConnection } from "../../../../lib/media-managers/queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { ConnectionNicknameField } from "./connection-nickname-field";

/** Selected first when it is one of the choices. */
const PREFERRED_KIND: MediaManagerKind = "deluno";

const ALL_KINDS: MediaManagerKind[] = ["radarr", "sonarr", "deluno", "native"];

/** What choosing each one means, without naming what Weir does internally. */
const KIND_BLURBS: Record<MediaManagerKind, string> = {
  radarr:
    "Lets Weir check what Radarr is still importing, and download a film again.",
  sonarr:
    "Lets Weir check what Sonarr is still importing, and download an episode again.",
  deluno: "Hands a file to Weir to work on, and waits to be told it is ready.",
  native: "Anything else that can send Weir a message.",
};

const ADDRESS_HINT = "The address you use to open it in a browser.";

type FormState = {
  kind: MediaManagerKind;
  base_url: string;
  api_key: string;
  nickname: string;
};

function emptyForm(kind: MediaManagerKind): FormState {
  return { kind, base_url: "", api_key: "", nickname: "" };
}

type AddConnectionProps = {
  /** Which media managers can be chosen. With just one, there is nothing to choose. */
  kinds?: MediaManagerKind[];
  onCancel: () => void;
  /** Called instead of `onCancel` once the media manager is actually created. */
  onCreated: (connection: MediaManagerConnection) => void;
  /** Reaches the choice of media manager, for a drawer that focuses it as it opens. */
  kindRef?: RefObject<HTMLSelectElement | null>;
};

/** The fields and buttons for adding a media manager, for a place that already has its own heading. */
export function AddConnectionFields({
  kinds = ALL_KINDS,
  onCancel,
  onCreated,
  kindRef,
}: AddConnectionProps) {
  const [form, setForm] = useState<FormState>(() =>
    emptyForm(kinds.includes(PREFERRED_KIND) ? PREFERRED_KIND : kinds[0]),
  );
  const create = useCreateMediaManagerConnection();
  const needsAddress = connectionNeedsAddress(form.kind);
  const change = <K extends keyof FormState>(key: K, value: FormState[K]) =>
    setForm((current) => ({ ...current, [key]: value }));

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        create.mutate(
          {
            ...form,
            nickname: form.nickname.trim(),
            enabled: true,
            downloaded_scan_enabled: false,
          },
          { onSuccess: onCreated },
        );
      }}
    >
      <div className="mm-quiet-stack">
        <div className="mm-field-row">
          {kinds.length > 1 ? (
            <Field
              label="Which media manager is it?"
              hint={KIND_BLURBS[form.kind]}
              width="medium"
            >
              <select
                ref={kindRef}
                data-testid="media-manager-kind"
                className="mm-input"
                value={form.kind}
                onChange={(e) =>
                  change("kind", e.target.value as MediaManagerKind)
                }
              >
                {kinds.map((kind) => (
                  <option key={kind} value={kind}>
                    {MEDIA_MANAGER_KIND_LABELS[kind]}
                  </option>
                ))}
              </select>
            </Field>
          ) : null}
        </div>
        <Field
          label="Where to find it"
          hint={needsAddress ? ADDRESS_HINT : OPTIONAL_ADDRESS_HINT}
          width="wide"
        >
          <input
            data-testid="media-manager-base-url"
            autoComplete="url"
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
            autoComplete="new-password"
            className="mm-input"
            value={form.api_key}
            onChange={(e) => change("api_key", e.target.value)}
          />
        </Field>
        <ConnectionNicknameField
          testId="media-manager-nickname"
          className="mm-input"
          value={form.nickname}
          onChange={(value) => change("nickname", value)}
        />
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
          disabled={create.isPending || (needsAddress && !form.base_url.trim())}
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
    </form>
  );
}

/** Adding a media manager in a card of its own, where the page has no drawer to put it in. */
export function AddConnectionForm(props: AddConnectionProps) {
  return (
    <QuietSection
      headingId="add-media-manager-heading"
      heading="Add a media manager"
      level={3}
    >
      <AddConnectionFields {...props} />
    </QuietSection>
  );
}
