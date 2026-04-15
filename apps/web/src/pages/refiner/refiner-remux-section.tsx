import { useEffect, useId, useState } from "react";
import { PageLoading } from "../../components/shared/page-loading";
import { isHttpErrorFromApi, isLikelyNetworkFailure } from "../../lib/api/error-guards";
import { useMeQuery } from "../../lib/auth/queries";
import {
  useRefinerFileRemuxPassEnqueueMutation,
  useRefinerRemuxRulesSettingsQuery,
  useRefinerRemuxRulesSettingsSaveMutation,
} from "../../lib/refiner/queries";
import { REFINER_STREAM_LANGUAGE_OPTIONS } from "../../lib/refiner/stream-language-options";
import type { RefinerRemuxRulesSettingsPutBody } from "../../lib/refiner/types";
import { MmListboxPicker, type MmListboxOption } from "../../components/ui/mm-listbox-picker";
import { MmMultiListboxPicker } from "../../components/ui/mm-multi-listbox-picker";
import { mmActionButtonClass, mmCheckboxControlClass, mmEditableTextFieldClass } from "../../lib/ui/mm-control-roles";

function canEditRefiner(role: string | undefined): boolean {
  return role === "operator" || role === "admin";
}

const KNOWN_REFINER_LANG_CODES = new Set(REFINER_STREAM_LANGUAGE_OPTIONS.map((o) => o.code));

function primaryLanguageOptions(currentCode: string): MmListboxOption[] {
  const cur = currentCode.trim();
  const out: MmListboxOption[] = [{ value: "", label: "Choose a language…" }];
  if (cur && !KNOWN_REFINER_LANG_CODES.has(cur)) {
    out.push({ value: cur, label: `${cur} (saved — not in standard list)` });
  }
  for (const { code, label } of REFINER_STREAM_LANGUAGE_OPTIONS) {
    out.push({ value: code, label: `${label} (${code})` });
  }
  return out;
}

function secondaryLanguageOptions(currentCode: string): MmListboxOption[] {
  const cur = currentCode.trim();
  const out: MmListboxOption[] = [{ value: "", label: "None" }];
  if (cur && !KNOWN_REFINER_LANG_CODES.has(cur)) {
    out.push({ value: cur, label: `${cur} (saved — not in standard list)` });
  }
  for (const { code, label } of REFINER_STREAM_LANGUAGE_OPTIONS) {
    out.push({ value: code, label: `${label} (${code})` });
  }
  return out;
}

const DEFAULT_AUDIO_SLOT_OPTIONS: MmListboxOption[] = [
  { value: "primary", label: "Primary" },
  { value: "secondary", label: "Secondary" },
];

const AUDIO_PREFERENCE_OPTIONS: MmListboxOption[] = [
  { value: "preferred_langs_quality", label: "Match your language list first, then best quality" },
  { value: "preferred_langs_strict", label: "Only keep tracks in your language list" },
  { value: "quality_all_languages", label: "Ignore language list and keep the best quality track" },
];

const SUBTITLE_MODE_OPTIONS: MmListboxOption[] = [
  { value: "remove_all", label: "Remove all subtitles in output" },
  { value: "keep_selected", label: "Keep selected languages only" },
];

const SUBTITLE_LANGUAGE_OPTIONS: MmListboxOption[] = REFINER_STREAM_LANGUAGE_OPTIONS.map((o) => ({
  value: o.code,
  label: `${o.label} (${o.code})`,
}));

function parseCsvCodes(raw: string): string[] {
  const seen = new Set<string>();
  const out: string[] = [];
  for (const token of raw.split(",")) {
    const v = token.trim().toLowerCase();
    if (!v || seen.has(v)) {
      continue;
    }
    seen.add(v);
    out.push(v);
  }
  return out;
}

/** Audio & subtitles tab: saved remux defaults + one-off file run. */
export function RefinerRemuxSection() {
  const me = useMeQuery();
  const q = useRefinerRemuxRulesSettingsQuery();
  const save = useRefinerRemuxRulesSettingsSaveMutation();
  const enqueue = useRefinerFileRemuxPassEnqueueMutation();

  const primaryLangLabelId = useId();
  const secondaryLangLabelId = useId();
  const tertiaryLangLabelId = useId();
  const defaultSlotLabelId = useId();
  const audioPolicyLabelId = useId();
  const subtitleModeLabelId = useId();
  const subtitleLanguagesLabelId = useId();

  const [draft, setDraft] = useState<RefinerRemuxRulesSettingsPutBody | null>(null);
  const [relPath, setRelPath] = useState("");
  const [enqueueDryRun, setEnqueueDryRun] = useState(true);
  const [enqueueMediaScope, setEnqueueMediaScope] = useState<"movie" | "tv">("movie");

  useEffect(() => {
    if (!q.data) {
      return;
    }
    const { updated_at: _u, ...rest } = q.data;
    setDraft(rest);
  }, [q.data]);

  const editable = canEditRefiner(me.data?.role);

  const dirty =
    draft !== null &&
    q.data !== undefined &&
    (draft.primary_audio_lang !== q.data.primary_audio_lang ||
      draft.secondary_audio_lang !== q.data.secondary_audio_lang ||
      draft.tertiary_audio_lang !== q.data.tertiary_audio_lang ||
      draft.default_audio_slot !== q.data.default_audio_slot ||
      draft.remove_commentary !== q.data.remove_commentary ||
      draft.subtitle_mode !== q.data.subtitle_mode ||
      draft.subtitle_langs_csv !== q.data.subtitle_langs_csv ||
      draft.preserve_forced_subs !== q.data.preserve_forced_subs ||
      draft.preserve_default_subs !== q.data.preserve_default_subs ||
      draft.audio_preference_mode !== q.data.audio_preference_mode);

  if (q.isPending || me.isPending) {
    return <PageLoading label="Loading audio and subtitle settings" />;
  }
  if (q.isError) {
    return (
      <div
        className="mm-fetcher-module-surface w-full min-w-0 rounded border border-red-900/40 bg-red-950/20 p-4 text-sm text-red-200"
        data-testid="refiner-remux-settings-error"
        role="alert"
      >
        <p className="font-semibold">Could not load audio and subtitle settings</p>
        <p className="mt-1">
          {isLikelyNetworkFailure(q.error)
            ? "Check that the MediaMop API is running."
            : isHttpErrorFromApi(q.error)
              ? "Sign in, then try again."
              : "Request failed."}
        </p>
      </div>
    );
  }

  if (!draft || !q.data) {
    return null;
  }

  const d = draft;
  const fieldDisabled = !editable || save.isPending;
  const selectedSubtitleCodes = parseCsvCodes(d.subtitle_langs_csv);

  return (
    <div className="flex w-full min-w-0 flex-col gap-10" data-testid="refiner-remux-section">
      <section
        className="mm-fetcher-module-surface w-full min-w-0 rounded border border-[var(--mm-border)] bg-[var(--mm-card-bg)] p-5 text-sm leading-relaxed text-[var(--mm-text2)] sm:p-6"
        aria-labelledby="refiner-audio-subtitles-saved-heading"
        data-testid="refiner-remux-defaults"
      >
        <h2 id="refiner-audio-subtitles-saved-heading" className="text-base font-semibold text-[var(--mm-text)]">
          Saved audio and subtitle defaults
        </h2>
        <p className="mt-2 text-[var(--mm-text3)]">
          Default audio and subtitles for <strong className="text-[var(--mm-text)]">Movies</strong> and{" "}
          <strong className="text-[var(--mm-text)]">TV</strong> file passes. They stay saved until you change them.
        </p>

        <div
          className="mt-4 rounded-md border border-[var(--mm-border)] bg-[var(--mm-accent-soft)]/10 px-3 py-2.5 text-xs text-[var(--mm-text3)]"
          role="note"
        >
          <p className="font-medium text-[var(--mm-text2)]">Live passes and your files</p>
          <p className="mt-1">
            After a successful <strong className="text-[var(--mm-text)]">live</strong> pass, MediaMop may delete only the
            watched-folder <strong className="text-[var(--mm-text)]">source media file</strong> that was remuxed. Dry
            runs and failures never delete sources. Refiner does not remove sidecars, siblings, or empty folders.
          </p>
        </div>

        {!editable ? (
          <p className="mt-3 text-xs text-[var(--mm-text3)]">Operators and admins can edit these rules.</p>
        ) : null}

        <div className="mt-5 grid gap-x-5 gap-y-5 sm:grid-cols-2">
          <div className="block sm:col-span-1">
            <span id={primaryLangLabelId} className="text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">
              Primary language
            </span>
            <MmListboxPicker
              ariaLabelledBy={primaryLangLabelId}
              disabled={fieldDisabled}
              placeholder="Choose a language…"
              options={primaryLanguageOptions(d.primary_audio_lang)}
              value={d.primary_audio_lang}
              onChange={(v) => setDraft({ ...d, primary_audio_lang: v })}
            />
          </div>
          <div className="block sm:col-span-1">
            <span id={secondaryLangLabelId} className="text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">
              Secondary language
            </span>
            <MmListboxPicker
              ariaLabelledBy={secondaryLangLabelId}
              disabled={fieldDisabled}
              placeholder="None"
              options={secondaryLanguageOptions(d.secondary_audio_lang)}
              value={d.secondary_audio_lang}
              onChange={(v) => setDraft({ ...d, secondary_audio_lang: v })}
            />
          </div>
          <div className="block sm:col-span-1">
            <span id={tertiaryLangLabelId} className="text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">
              Tertiary language
            </span>
            <MmListboxPicker
              ariaLabelledBy={tertiaryLangLabelId}
              disabled={fieldDisabled}
              placeholder="None"
              options={secondaryLanguageOptions(d.tertiary_audio_lang)}
              value={d.tertiary_audio_lang}
              onChange={(v) => setDraft({ ...d, tertiary_audio_lang: v })}
            />
          </div>
          <div className="block sm:col-span-1">
            <span id={defaultSlotLabelId} className="text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">
              Default audio slot
            </span>
            <MmListboxPicker
              ariaLabelledBy={defaultSlotLabelId}
              disabled={fieldDisabled}
              options={DEFAULT_AUDIO_SLOT_OPTIONS}
              value={d.default_audio_slot}
              onChange={(v) =>
                setDraft({ ...d, default_audio_slot: v as RefinerRemuxRulesSettingsPutBody["default_audio_slot"] })
              }
            />
          </div>
          <div className="block sm:col-span-2">
            <span id={audioPolicyLabelId} className="text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">
              Audio selection policy
            </span>
            <MmListboxPicker
              ariaLabelledBy={audioPolicyLabelId}
              disabled={fieldDisabled}
              options={AUDIO_PREFERENCE_OPTIONS}
              value={d.audio_preference_mode}
              onChange={(v) =>
                setDraft({
                  ...d,
                  audio_preference_mode: v as RefinerRemuxRulesSettingsPutBody["audio_preference_mode"],
                })
              }
            />
          </div>
          <label className="flex cursor-pointer items-start gap-3 sm:col-span-1">
            <input
              type="checkbox"
              className={mmCheckboxControlClass}
              checked={d.remove_commentary}
              disabled={fieldDisabled}
              onChange={(e) => setDraft({ ...d, remove_commentary: e.target.checked })}
            />
            <span className="text-sm text-[var(--mm-text2)]">Strip commentary when planning</span>
          </label>
          <div className="block sm:col-span-2">
            <span id={subtitleModeLabelId} className="text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">
              Subtitles
            </span>
            <MmListboxPicker
              ariaLabelledBy={subtitleModeLabelId}
              disabled={fieldDisabled}
              options={SUBTITLE_MODE_OPTIONS}
              value={d.subtitle_mode}
              onChange={(v) =>
                setDraft({ ...d, subtitle_mode: v as RefinerRemuxRulesSettingsPutBody["subtitle_mode"] })
              }
            />
          </div>
          <label className="block sm:col-span-2">
            <span id={subtitleLanguagesLabelId} className="text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">
              Subtitle languages
            </span>
            <MmMultiListboxPicker
              ariaLabelledBy={subtitleLanguagesLabelId}
              options={SUBTITLE_LANGUAGE_OPTIONS}
              values={selectedSubtitleCodes}
              disabled={fieldDisabled || d.subtitle_mode === "remove_all"}
              onChange={(next) => setDraft({ ...d, subtitle_langs_csv: next.join(",") })}
              placeholder="Select subtitle languages"
              data-testid="refiner-subtitle-language-picker"
            />
            <p className="mt-1 text-xs text-[var(--mm-text3)]">
              Pick one or more subtitle languages to keep when subtitle mode is set to keep selected.
            </p>
          </label>
          <label className="flex cursor-pointer items-start gap-3 sm:col-span-1">
            <input
              type="checkbox"
              className={mmCheckboxControlClass}
              checked={d.preserve_forced_subs}
              disabled={fieldDisabled || d.subtitle_mode === "remove_all"}
              onChange={(e) => setDraft({ ...d, preserve_forced_subs: e.target.checked })}
            />
            <span className="text-sm text-[var(--mm-text2)]">Keep forced subtitles when keeping subtitles</span>
          </label>
          <label className="flex cursor-pointer items-start gap-3 sm:col-span-1">
            <input
              type="checkbox"
              className={mmCheckboxControlClass}
              checked={d.preserve_default_subs}
              disabled={fieldDisabled || d.subtitle_mode === "remove_all"}
              onChange={(e) => setDraft({ ...d, preserve_default_subs: e.target.checked })}
            />
            <span className="text-sm text-[var(--mm-text2)]">Keep default subtitles when keeping subtitles</span>
          </label>
        </div>

        {save.isError ? (
          <p className="mt-3 text-sm text-red-300" role="alert" data-testid="refiner-remux-save-error">
            {save.error instanceof Error ? save.error.message : "Save failed."}
          </p>
        ) : null}

        {save.isSuccess && !dirty ? (
          <p className="mt-3 text-xs text-[var(--mm-text3)]" data-testid="refiner-remux-saved-hint">
            Saved.
          </p>
        ) : null}

        <div className="mt-6">
          <button
            type="button"
            className={mmActionButtonClass({
              variant: "primary",
              disabled: !editable || !dirty || save.isPending,
            })}
            disabled={!editable || !dirty || save.isPending}
            onClick={() => save.mutate(d)}
          >
            {save.isPending ? "Saving…" : "Save audio/subtitle defaults"}
          </button>
        </div>
      </section>

      <section
        className="mm-fetcher-module-surface w-full min-w-0 rounded border border-[var(--mm-border)] bg-[var(--mm-card-bg)] p-5 text-sm leading-relaxed text-[var(--mm-text2)] sm:p-6"
        aria-labelledby="refiner-run-one-file-heading"
        data-testid="refiner-remux-file-enqueue"
      >
        <h2 id="refiner-run-one-file-heading" className="text-base font-semibold text-[var(--mm-text)]">
          Run one file now
        </h2>
        <p className="mt-2 text-[var(--mm-text3)]">
          Use this when you want to test one file before scanning a full library. Enter a path under the saved{" "}
          <strong className="text-[var(--mm-text)]">Movies</strong> or <strong className="text-[var(--mm-text)]">TV</strong>{" "}
          watched folder. Dry run only probes/plans. Live writes output and may remove the source file after success.
        </p>

        {!editable ? (
          <p className="mt-3 text-xs text-[var(--mm-text3)]">Operators and admins can queue passes.</p>
        ) : null}

        <div className="mt-5 space-y-4">
          <fieldset className="space-y-2">
            <legend className="text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">Path scope</legend>
            <label className="flex cursor-pointer items-center gap-2">
              <input
                type="radio"
                name="refiner-run-one-file-scope"
                checked={enqueueMediaScope === "movie"}
                disabled={!editable || enqueue.isPending}
                onChange={() => setEnqueueMediaScope("movie")}
              />
              <span>Movies watched folder</span>
            </label>
            <label className="flex cursor-pointer items-center gap-2">
              <input
                type="radio"
                name="refiner-run-one-file-scope"
                checked={enqueueMediaScope === "tv"}
                disabled={!editable || enqueue.isPending}
                onChange={() => setEnqueueMediaScope("tv")}
              />
              <span>TV watched folder</span>
            </label>
          </fieldset>
          <label className="block">
            <span className="text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">
              Relative media path
            </span>
            <input
              className={`${mmEditableTextFieldClass} mt-1`}
              value={relPath}
              disabled={!editable || enqueue.isPending}
              onChange={(e) => setRelPath(e.target.value)}
              placeholder="ExampleFolder/Release/file.mkv"
            />
          </label>
          <label className="flex cursor-pointer items-start gap-3">
            <input
              type="checkbox"
              className={mmCheckboxControlClass}
              checked={enqueueDryRun}
              disabled={!editable || enqueue.isPending}
              onChange={(e) => setEnqueueDryRun(e.target.checked)}
            />
            <span className="text-sm text-[var(--mm-text2)]">Dry run (recommended)</span>
          </label>
        </div>

        {enqueue.isError ? (
          <p className="mt-3 text-sm text-red-300" role="alert" data-testid="refiner-remux-enqueue-error">
            {enqueue.error instanceof Error ? enqueue.error.message : "Enqueue failed."}
          </p>
        ) : null}

        {enqueue.isSuccess ? (
          <p className="mt-3 text-xs text-[var(--mm-text3)]" data-testid="refiner-remux-enqueued-hint">
            Queued job #{enqueue.data.job_id}. When workers run, check Activity for the result.
          </p>
        ) : null}

        <div className="mt-6">
          <button
            type="button"
            className={mmActionButtonClass({
              variant: "secondary",
              disabled: !editable || !relPath.trim() || enqueue.isPending,
            })}
            disabled={!editable || !relPath.trim() || enqueue.isPending}
            onClick={() =>
              enqueue.mutate({
                relative_media_path: relPath.trim(),
                dry_run: enqueueDryRun,
                media_scope: enqueueMediaScope,
              })
            }
          >
            {enqueue.isPending ? "Queuing…" : "Run one file"}
          </button>
        </div>
      </section>
    </div>
  );
}
