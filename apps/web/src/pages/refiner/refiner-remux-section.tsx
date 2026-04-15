import { useEffect, useState } from "react";
import { MmListboxPicker, type MmListboxOption } from "../../components/ui/mm-listbox-picker";
import { MmMultiListboxPicker } from "../../components/ui/mm-multi-listbox-picker";
import { PageLoading } from "../../components/shared/page-loading";
import { useMeQuery } from "../../lib/auth/queries";
import { isHttpErrorFromApi, isLikelyNetworkFailure } from "../../lib/api/error-guards";
import {
  useRefinerFileRemuxPassEnqueueMutation,
  useRefinerRemuxRulesSettingsQuery,
  useRefinerRemuxRulesSettingsSaveMutation,
} from "../../lib/refiner/queries";
import { REFINER_STREAM_LANGUAGE_OPTIONS } from "../../lib/refiner/stream-language-options";
import type { RefinerRemuxRulesScopeSettings, RefinerRemuxRulesSettingsPutBody } from "../../lib/refiner/types";
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
  { value: "remove_all", label: "Remove all subtitles" },
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

type ScopeCardProps = {
  title: "Movies" | "TV";
  draft: RefinerRemuxRulesScopeSettings;
  setDraft: (next: RefinerRemuxRulesScopeSettings) => void;
  editable: boolean;
  isPending: boolean;
  dirty: boolean;
  isError: boolean;
  error: unknown;
  isSuccess: boolean;
  onSave: () => void;
  pickerTestId: string;
  saveLabel: string;
};

function ScopeCard({
  title,
  draft,
  setDraft,
  editable,
  isPending,
  dirty,
  isError,
  error,
  isSuccess,
  onSave,
  pickerTestId,
  saveLabel,
}: ScopeCardProps) {
  const disabled = !editable || isPending;
  const subtitleCodes = parseCsvCodes(draft.subtitle_langs_csv);
  return (
    <section className="rounded-md border border-[var(--mm-border)] bg-[var(--mm-card-bg)]/70 p-5">
      <div className="border-b border-[var(--mm-border)] pb-3">
        <p className="text-[0.7rem] font-semibold uppercase tracking-[0.16em] text-[var(--mm-text3)]">Scope</p>
        <h3 className="mt-1 text-sm font-semibold text-[var(--mm-text1)]">{title}</h3>
      </div>
      <div className="mt-4 space-y-5">
        <section>
          <h4 className="text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">Audio</h4>
          <div className="mt-3 grid gap-x-5 gap-y-4 sm:grid-cols-2">
            <div className="sm:col-span-1">
              <span className="text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">Primary language</span>
              <MmListboxPicker
                disabled={disabled}
                placeholder="Choose a language…"
                options={primaryLanguageOptions(draft.primary_audio_lang)}
                value={draft.primary_audio_lang}
                onChange={(v) => setDraft({ ...draft, primary_audio_lang: v })}
              />
            </div>
            <div className="sm:col-span-1">
              <span className="text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">Secondary language</span>
              <MmListboxPicker
                disabled={disabled}
                placeholder="None"
                options={secondaryLanguageOptions(draft.secondary_audio_lang)}
                value={draft.secondary_audio_lang}
                onChange={(v) => setDraft({ ...draft, secondary_audio_lang: v })}
              />
            </div>
            <div className="sm:col-span-1">
              <span className="text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">Tertiary language</span>
              <MmListboxPicker
                disabled={disabled}
                placeholder="None"
                options={secondaryLanguageOptions(draft.tertiary_audio_lang)}
                value={draft.tertiary_audio_lang}
                onChange={(v) => setDraft({ ...draft, tertiary_audio_lang: v })}
              />
            </div>
            <div className="sm:col-span-1">
              <span className="text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">Default audio slot</span>
              <MmListboxPicker
                disabled={disabled}
                options={DEFAULT_AUDIO_SLOT_OPTIONS}
                value={draft.default_audio_slot}
                onChange={(v) => setDraft({ ...draft, default_audio_slot: v as RefinerRemuxRulesScopeSettings["default_audio_slot"] })}
              />
            </div>
            <div className="sm:col-span-2">
              <span className="text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">Audio selection policy</span>
              <MmListboxPicker
                disabled={disabled}
                options={AUDIO_PREFERENCE_OPTIONS}
                value={draft.audio_preference_mode}
                onChange={(v) =>
                  setDraft({ ...draft, audio_preference_mode: v as RefinerRemuxRulesScopeSettings["audio_preference_mode"] })
                }
              />
            </div>
            <label className="flex cursor-pointer items-start gap-3 sm:col-span-2">
              <input
                type="checkbox"
                className={mmCheckboxControlClass}
                checked={draft.remove_commentary}
                disabled={disabled}
                onChange={(e) => setDraft({ ...draft, remove_commentary: e.target.checked })}
              />
              <span className="text-sm text-[var(--mm-text2)]">Remove commentary tracks</span>
            </label>
          </div>
        </section>
        <section>
          <h4 className="text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">Subtitles</h4>
          <div className="mt-3 grid gap-x-5 gap-y-4 sm:grid-cols-2">
            <div className="sm:col-span-2">
              <MmListboxPicker
                disabled={disabled}
                options={SUBTITLE_MODE_OPTIONS}
                value={draft.subtitle_mode}
                onChange={(v) => setDraft({ ...draft, subtitle_mode: v as RefinerRemuxRulesScopeSettings["subtitle_mode"] })}
              />
            </div>
            <label className="block sm:col-span-2">
              <span className="text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">Subtitle languages</span>
              <MmMultiListboxPicker
                options={SUBTITLE_LANGUAGE_OPTIONS}
                values={subtitleCodes}
                disabled={disabled}
                onChange={(next) => setDraft({ ...draft, subtitle_langs_csv: next.join(",") })}
                placeholder="Select subtitle languages"
                data-testid={pickerTestId}
              />
            </label>
            <label className="flex cursor-pointer items-start gap-3 sm:col-span-1">
              <input
                type="checkbox"
                className={mmCheckboxControlClass}
                checked={draft.preserve_forced_subs}
                disabled={disabled || draft.subtitle_mode === "remove_all"}
                onChange={(e) => setDraft({ ...draft, preserve_forced_subs: e.target.checked })}
              />
              <span className="text-sm text-[var(--mm-text2)]">Keep forced subtitles</span>
            </label>
            <label className="flex cursor-pointer items-start gap-3 sm:col-span-1">
              <input
                type="checkbox"
                className={mmCheckboxControlClass}
                checked={draft.preserve_default_subs}
                disabled={disabled || draft.subtitle_mode === "remove_all"}
                onChange={(e) => setDraft({ ...draft, preserve_default_subs: e.target.checked })}
              />
              <span className="text-sm text-[var(--mm-text2)]">Keep default subtitles</span>
            </label>
          </div>
        </section>
      </div>
      {isError ? (
        <p className="mt-3 text-sm text-red-300" role="alert">
          {error instanceof Error ? error.message : "Save failed."}
        </p>
      ) : null}
      {isSuccess && !dirty ? <p className="mt-3 text-xs text-[var(--mm-text3)]">Saved.</p> : null}
      <div className="mt-5 rounded-md border border-[var(--mm-border)] bg-black/10 px-3 py-3">
        <button
          type="button"
          className={mmActionButtonClass({
            variant: "primary",
            disabled: !editable || !dirty || isPending,
          })}
          disabled={!editable || !dirty || isPending}
          onClick={onSave}
        >
          {isPending ? "Saving…" : saveLabel}
        </button>
      </div>
    </section>
  );
}

/** Audio & subtitles tab: saved remux defaults + one-off file run. */
export function RefinerRemuxSection() {
  const me = useMeQuery();
  const q = useRefinerRemuxRulesSettingsQuery();
  const saveMovie = useRefinerRemuxRulesSettingsSaveMutation();
  const saveTv = useRefinerRemuxRulesSettingsSaveMutation();
  const enqueue = useRefinerFileRemuxPassEnqueueMutation();

  const [movieDraft, setMovieDraft] = useState<RefinerRemuxRulesScopeSettings | null>(null);
  const [tvDraft, setTvDraft] = useState<RefinerRemuxRulesScopeSettings | null>(null);
  const [relPath, setRelPath] = useState("");
  const [enqueueDryRun, setEnqueueDryRun] = useState(true);
  const [enqueueMediaScope, setEnqueueMediaScope] = useState<"movie" | "tv">("movie");

  useEffect(() => {
    if (!q.data) {
      return;
    }
    setMovieDraft(q.data.movie);
    setTvDraft(q.data.tv);
  }, [q.data]);

  const editable = canEditRefiner(me.data?.role);
  const movieDirty =
    movieDraft !== null &&
    q.data !== undefined &&
    JSON.stringify(movieDraft) !== JSON.stringify(q.data.movie);
  const tvDirty = tvDraft !== null && q.data !== undefined && JSON.stringify(tvDraft) !== JSON.stringify(q.data.tv);

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
  if (!movieDraft || !tvDraft) {
    return null;
  }

  const saveMovieBody: RefinerRemuxRulesSettingsPutBody = { media_scope: "movie", ...movieDraft };
  const saveTvBody: RefinerRemuxRulesSettingsPutBody = { media_scope: "tv", ...tvDraft };

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
          Movies and TV keep separate defaults. Saving one scope never changes the other.
        </p>
        <div className="mt-4 rounded-md border border-[var(--mm-border)] bg-black/10 px-3 py-2.5 text-xs text-[var(--mm-text3)]" role="note">
          After a successful <strong className="text-[var(--mm-text)]">live</strong> pass, MediaMop may delete only the
          watched-folder <strong className="text-[var(--mm-text)]">source media file</strong> that was remuxed.
        </div>
        {!editable ? <p className="mt-3 text-xs text-[var(--mm-text3)]">Operators and admins can edit these rules.</p> : null}
        <div className="mt-6 grid gap-5 lg:grid-cols-2">
          <ScopeCard
            title="Movies"
            draft={movieDraft}
            setDraft={setMovieDraft}
            editable={editable}
            isPending={saveMovie.isPending}
            dirty={movieDirty}
            isError={saveMovie.isError}
            error={saveMovie.error}
            isSuccess={saveMovie.isSuccess}
            onSave={() => saveMovie.mutate(saveMovieBody)}
            pickerTestId="refiner-subtitle-language-picker-movie"
            saveLabel="Save Movies audio/subtitle defaults"
          />
          <ScopeCard
            title="TV"
            draft={tvDraft}
            setDraft={setTvDraft}
            editable={editable}
            isPending={saveTv.isPending}
            dirty={tvDirty}
            isError={saveTv.isError}
            error={saveTv.error}
            isSuccess={saveTv.isSuccess}
            onSave={() => saveTv.mutate(saveTvBody)}
            pickerTestId="refiner-subtitle-language-picker-tv"
            saveLabel="Save TV audio/subtitle defaults"
          />
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
          watched folder.
        </p>
        {!editable ? <p className="mt-3 text-xs text-[var(--mm-text3)]">Operators and admins can queue passes.</p> : null}
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
            <span className="text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">Relative media path</span>
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
