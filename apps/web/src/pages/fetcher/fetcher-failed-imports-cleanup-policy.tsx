import { useEffect, useState } from "react";
import { showFailedImportCleanupPolicyEditor } from "../../lib/fetcher/failed-imports/eligibility";
import {
  useFailedImportCleanupPolicyQuery,
  useFailedImportCleanupPolicySaveMutation,
} from "../../lib/fetcher/failed-imports/queries";
import type { FailedImportCleanupPolicyAxis } from "../../lib/fetcher/failed-imports/types";
import {
  FETCHER_FI_POLICY_CARD_LEAD,
  FETCHER_FI_POLICY_CARD_TITLE,
  FETCHER_FI_POLICY_MOVIES_HEADING,
  FETCHER_FI_POLICY_SAVE,
  FETCHER_FI_POLICY_SAVED_HINT,
  FETCHER_FI_POLICY_SAVING,
  FETCHER_FI_POLICY_STORAGE_NOTE,
  FETCHER_FI_POLICY_TOGGLE_CORRUPT,
  FETCHER_FI_POLICY_TOGGLE_DOWNLOAD_FAILED,
  FETCHER_FI_POLICY_TOGGLE_IMPORT_FAILED,
  FETCHER_FI_POLICY_TOGGLE_QUALITY,
  FETCHER_FI_POLICY_TOGGLE_UNMATCHED,
  FETCHER_FI_POLICY_TV_HEADING,
  FETCHER_FI_POLICY_VIEWER_NOTE,
} from "../../lib/fetcher/failed-imports/user-copy";

function cloneAxis(a: FailedImportCleanupPolicyAxis): FailedImportCleanupPolicyAxis {
  return { ...a };
}

function PolicyAxisBlock({
  title,
  value,
  onChange,
  disabled,
}: {
  title: string;
  value: FailedImportCleanupPolicyAxis;
  onChange: (next: FailedImportCleanupPolicyAxis) => void;
  disabled: boolean;
}) {
  const row = (field: keyof FailedImportCleanupPolicyAxis, label: string) => (
    <label
      key={field}
      className="flex cursor-pointer items-start gap-2 rounded px-1 py-1 hover:bg-[var(--mm-card-bg)]"
    >
      <input
        type="checkbox"
        className="mt-1"
        checked={value[field]}
        disabled={disabled}
        onChange={(e) => onChange({ ...value, [field]: e.target.checked })}
      />
      <span className="text-sm text-[var(--mm-text2)]">{label}</span>
    </label>
  );

  return (
    <div className="space-y-2">
      <h3 className="text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">{title}</h3>
      <div className="flex flex-col gap-1">
        {row("remove_quality_rejections", FETCHER_FI_POLICY_TOGGLE_QUALITY)}
        {row("remove_unmatched_manual_import_rejections", FETCHER_FI_POLICY_TOGGLE_UNMATCHED)}
        {row("remove_corrupt_imports", FETCHER_FI_POLICY_TOGGLE_CORRUPT)}
        {row("remove_failed_downloads", FETCHER_FI_POLICY_TOGGLE_DOWNLOAD_FAILED)}
        {row("remove_failed_imports", FETCHER_FI_POLICY_TOGGLE_IMPORT_FAILED)}
      </div>
    </div>
  );
}

/** Fetcher: removal rules for Radarr/Sonarr download-queue failed-import passes. */
export function FetcherFailedImportsCleanupPolicySection({ role }: { role: string | undefined }) {
  const q = useFailedImportCleanupPolicyQuery();
  const save = useFailedImportCleanupPolicySaveMutation();
  const canEdit = showFailedImportCleanupPolicyEditor(role);

  const [movies, setMovies] = useState<FailedImportCleanupPolicyAxis | null>(null);
  const [tvShows, setTvShows] = useState<FailedImportCleanupPolicyAxis | null>(null);

  useEffect(() => {
    if (q.data) {
      setMovies(cloneAxis(q.data.movies));
      setTvShows(cloneAxis(q.data.tv_shows));
    }
  }, [q.data]);

  const dirty =
    q.data &&
    movies &&
    tvShows &&
    (JSON.stringify(movies) !== JSON.stringify(q.data.movies) ||
      JSON.stringify(tvShows) !== JSON.stringify(q.data.tv_shows));

  return (
    <section
      className="mm-card mm-dash-card mm-fetcher-module-surface mb-6"
      aria-labelledby="mm-fetcher-fi-policy-heading"
      data-testid="fetcher-failed-imports-cleanup-policy"
    >
      <h2 id="mm-fetcher-fi-policy-heading" className="mm-card__title">
        {FETCHER_FI_POLICY_CARD_TITLE}
      </h2>
      <p className="mm-card__body mm-card__body--tight text-sm text-[var(--mm-text3)]">{FETCHER_FI_POLICY_CARD_LEAD}</p>

      {q.isPending ? (
        <p className="mm-card__body text-sm text-[var(--mm-text3)]" data-testid="fetcher-failed-imports-policy-loading">
          Loading removal rules…
        </p>
      ) : q.isError ? (
        <p className="mm-card__body text-sm text-red-400" data-testid="fetcher-failed-imports-policy-error" role="alert">
          {q.error instanceof Error ? q.error.message : "Could not load removal rules."}
        </p>
      ) : q.data && movies && tvShows ? (
        <div className="mm-card__body mm-card__body--tight space-y-4">
          <p className="text-xs text-[var(--mm-text3)]" data-testid="fetcher-failed-imports-policy-storage-note">
            {FETCHER_FI_POLICY_STORAGE_NOTE}
          </p>
          {!canEdit ? (
            <p className="text-sm text-[var(--mm-text3)]">{FETCHER_FI_POLICY_VIEWER_NOTE}</p>
          ) : null}
          <div className="grid gap-6 sm:grid-cols-2">
            <PolicyAxisBlock
              title={FETCHER_FI_POLICY_MOVIES_HEADING}
              value={movies}
              onChange={setMovies}
              disabled={!canEdit || save.isPending}
            />
            <PolicyAxisBlock
              title={FETCHER_FI_POLICY_TV_HEADING}
              value={tvShows}
              onChange={setTvShows}
              disabled={!canEdit || save.isPending}
            />
          </div>
          {canEdit ? (
            <div className="border-t border-[var(--mm-border)] pt-3 space-y-2">
              <button
                type="button"
                data-testid="fetcher-failed-imports-policy-save"
                className="rounded border border-[var(--mm-border)] bg-[var(--mm-slate)] px-3 py-1.5 text-sm font-medium text-[var(--mm-text)] hover:bg-[var(--mm-card-bg)] disabled:opacity-50"
                disabled={!dirty || save.isPending}
                onClick={() => save.mutate({ movies, tv_shows: tvShows })}
              >
                {save.isPending ? FETCHER_FI_POLICY_SAVING : FETCHER_FI_POLICY_SAVE}
              </button>
              <p className="text-xs text-[var(--mm-text3)]">{FETCHER_FI_POLICY_SAVED_HINT}</p>
              {save.isError ? (
                <p className="text-sm text-red-400" role="alert">
                  {save.error instanceof Error ? save.error.message : "Save failed."}
                </p>
              ) : null}
              {save.isSuccess && !save.isPending && !dirty ? (
                <p className="text-sm text-[var(--mm-text2)]" data-testid="fetcher-failed-imports-policy-saved">
                  Saved.
                </p>
              ) : null}
            </div>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}
