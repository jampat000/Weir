import { useEffect, useState } from "react";

import { Field } from "../../../../components/shared/field";
import { QuietSection } from "../../../../components/shared/quiet-section";
import { errorMessage } from "../../../../lib/api/error-message";
import type { ProcessingMetadataProvider } from "../../../../lib/processing/metadata-provider-api";
import {
  useSaveProcessingMetadataProvider,
  useTestProcessingMetadataProvider,
} from "../../../../lib/processing/metadata-provider-queries";
import {
  mmActionButtonClass,
  mmCheckboxControlClass,
} from "../../../../lib/ui/mm-control-roles";

type ProviderName = "" | "tmdb";

export type ProviderDraft = ReturnType<typeof useProviderDraft>;

/** The provider form, kept by the Rules tab because the original-language rule names the provider too. */
export function useProviderDraft(
  saved: ProcessingMetadataProvider | undefined,
) {
  const [name, setName] = useState<ProviderName>("");
  const [baseUrl, setBaseUrl] = useState("");
  const [key, setKey] = useState("");
  const [clearKey, setClearKey] = useState(false);

  useEffect(() => {
    if (!saved) return;
    setName(saved.provider === "tmdb" ? "tmdb" : "");
    setBaseUrl(saved.base_url);
    setKey("");
    setClearKey(false);
  }, [saved]);

  return {
    name,
    setName,
    baseUrl,
    setBaseUrl,
    key,
    setKey,
    clearKey,
    setClearKey,
    /** A blank key keeps the saved one; clearing sends an empty key. */
    body: () => ({
      provider: name,
      base_url: baseUrl.trim(),
      ...(clearKey
        ? { api_key: "" }
        : key.trim()
          ? { api_key: key.trim() }
          : {}),
    }),
    keySent: () => {
      setKey("");
      setClearKey(false);
    },
  };
}

/**
 * The metadata provider the original-language rule asks which language a title was made in. That rule
 * is folded away, so the provider's editor is closed until someone opens it.
 */
export function MetadataProviderSection({
  saved,
  draft,
  editable,
}: {
  saved: ProcessingMetadataProvider | undefined;
  draft: ProviderDraft;
  editable: boolean;
}) {
  const saveProvider = useSaveProcessingMetadataProvider();
  const testProvider = useTestProcessingMetadataProvider();
  const [open, setOpen] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  const save = () => {
    setNotice(null);
    saveProvider.mutate(draft.body(), {
      onSuccess: (result) => {
        draft.keySent();
        setNotice(
          result.provider
            ? "Metadata provider saved. Test it before relying on original-language matching."
            : "Metadata provider cleared. Original-language rules will fall back to the saved audio preferences.",
        );
      },
      onError: (error) =>
        setNotice(
          errorMessage(error, "The metadata provider could not be saved."),
        ),
    });
  };

  const saveAndTest = () => {
    setNotice(null);
    saveProvider
      .mutateAsync(draft.body())
      .then(() => testProvider.mutateAsync(draft.body()))
      .then((result) => setNotice(result.detail))
      .catch((error: unknown) =>
        setNotice(errorMessage(error, "The metadata provider test failed.")),
      );
  };

  return (
    <QuietSection
      headingId="processing-rule-set-provider-heading"
      heading="Metadata provider"
      aside={
        <>
          <span className="mm-quiet-badge">
            {draft.name
              ? `${draft.name.toUpperCase()} configured`
              : "Not configured"}
          </span>
          <button
            type="button"
            className="mm-quiet-link"
            aria-expanded={open}
            onClick={() => setOpen((current) => !current)}
          >
            {open ? "Close →" : "Configure →"}
          </button>
        </>
      }
    >
      <p className="mm-quiet-note">
        Only needed by profiles that keep a title&apos;s original language. Weir
        falls back safely when metadata is unavailable.
      </p>

      {open ? (
        <div className="mt-5 border-t border-mm-border pt-5">
          <div className="mm-field-row">
            <Field label="Provider" width="medium">
              <select
                className="mm-input"
                value={draft.name}
                disabled={!editable}
                onChange={(event) =>
                  draft.setName(event.target.value === "tmdb" ? "tmdb" : "")
                }
              >
                <option value="">None</option>
                <option value="tmdb">TMDb</option>
              </select>
            </Field>
            <Field label="Provider or gateway URL" width="wide">
              <input
                className="mm-input"
                value={draft.baseUrl}
                disabled={!editable || draft.name === ""}
                onChange={(event) => draft.setBaseUrl(event.target.value)}
              />
            </Field>
            <Field label="API key" width="medium">
              <input
                type="password"
                className="mm-input"
                value={draft.key}
                disabled={!editable || draft.name === "" || draft.clearKey}
                placeholder={
                  saved?.key_configured
                    ? "Saved — enter a replacement only"
                    : "Enter API key"
                }
                onChange={(event) => draft.setKey(event.target.value)}
              />
            </Field>
            <label className="mm-provider-clear-key">
              <input
                type="checkbox"
                className={mmCheckboxControlClass}
                checked={draft.clearKey}
                disabled={!editable || !saved?.key_configured}
                onChange={(event) => draft.setClearKey(event.target.checked)}
              />
              Remove saved key on save
            </label>
          </div>
          {notice ? (
            <p role="status" className="mt-3 text-sm font-medium text-mm-text1">
              {notice}
            </p>
          ) : null}
          <div className="mt-4 flex flex-wrap gap-2">
            <button
              type="button"
              className={mmActionButtonClass({ variant: "primary" })}
              disabled={!editable || saveProvider.isPending}
              onClick={save}
            >
              Save provider
            </button>
            <button
              type="button"
              className={mmActionButtonClass({ variant: "secondary" })}
              disabled={
                !editable || draft.name === "" || testProvider.isPending
              }
              onClick={saveAndTest}
            >
              Save and test
            </button>
          </div>
        </div>
      ) : null}
    </QuietSection>
  );
}
