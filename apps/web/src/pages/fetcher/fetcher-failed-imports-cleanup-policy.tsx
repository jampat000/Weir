import { useEffect, useRef, useState } from "react";
import { showFailedImportCleanupPolicyEditor } from "../../lib/fetcher/failed-imports/eligibility";
import {
  useFailedImportCleanupPolicyQuery,
  useFailedImportCleanupPolicySaveMoviesMutation,
  useFailedImportCleanupPolicySaveTvMutation,
} from "../../lib/fetcher/failed-imports/queries";
import type {
  FailedImportCleanupPolicyAxis,
  FailedImportQueueHandlingAction,
} from "../../lib/fetcher/failed-imports/types";
import { fetcherMenuButtonClass } from "./fetcher-menu-button";
import {
  FETCHER_FI_ACTION_OPTIONS,
  FETCHER_FI_POLICY_ACTION_LEGEND,
  FETCHER_FI_POLICY_AUTOMATIC_CHECK_SUBHEADING,
  FETCHER_FI_POLICY_ROW_CORRUPT,
  FETCHER_FI_POLICY_ROW_DOWNLOAD_FAILED,
  FETCHER_FI_POLICY_ROW_GENERIC_IMPORT,
  FETCHER_FI_POLICY_ROW_MANUAL_IMPORT,
  FETCHER_FI_POLICY_ROW_QUALITY,
  FETCHER_FI_POLICY_ROW_SAMPLE,
  FETCHER_FI_POLICY_CARD_LEAD,
  FETCHER_FI_POLICY_CARD_LEAD_SECOND,
  FETCHER_FI_POLICY_CARD_TITLE,
  FETCHER_FI_POLICY_MOVIES_HEADING,
  FETCHER_FI_POLICY_NO_OPTIONS_ON,
  FETCHER_FI_POLICY_OPTIONS_GROUP_LABEL,
  FETCHER_FI_POLICY_QUEUE_ACTIONS_SUBHEADING,
  FETCHER_FI_POLICY_RUN_INTERVAL_HELPER,
  FETCHER_FI_POLICY_RUN_INTERVAL_LABEL,
  FETCHER_FI_POLICY_SAVE_RADARR,
  FETCHER_FI_POLICY_SAVE_SONARR,
  FETCHER_FI_POLICY_SAVING,
  FETCHER_FI_POLICY_TV_HEADING,
  FETCHER_FI_POLICY_VIEWER_NOTE,
} from "../../lib/fetcher/failed-imports/user-copy";
import { draftDiffersFromCommittedLabel } from "./fetcher-numeric-settings-draft";

type HandlingField = keyof Pick<
  FailedImportCleanupPolicyAxis,
  | "handling_quality_rejection"
  | "handling_unmatched_manual_import"
  | "handling_sample_release"
  | "handling_corrupt_import"
  | "handling_failed_download"
  | "handling_failed_import"
>;

const HANDLING_FIELDS: HandlingField[] = [
  "handling_quality_rejection",
  "handling_unmatched_manual_import",
  "handling_sample_release",
  "handling_corrupt_import",
  "handling_failed_download",
  "handling_failed_import",
];

const RUN_INTERVAL_MIN_MINUTES = 0;
const RUN_INTERVAL_MAX_MINUTES = 7 * 24 * 60;

/** Blank failed-import cleanup run interval restores to 60 minutes (timed cleanup on). */
const DEFAULT_FAILED_IMPORT_CLEANUP_RUN_INTERVAL_SECONDS = 60 * 60;

function clampCleanupIntervalSeconds(seconds: number): number {
  return Math.min(Math.max(Math.round(seconds), 60), 7 * 24 * 3600);
}

function committedCleanupRunIntervalMinutesText(axis: FailedImportCleanupPolicyAxis): string {
  if (!axis.cleanup_drive_schedule_enabled) {
    return "";
  }
  return String(Math.round(axis.cleanup_drive_schedule_interval_seconds / 60));
}

function finalizeFailedImportCleanupRunIntervalDraft(
  draft: string | null,
  value: FailedImportCleanupPolicyAxis,
): FailedImportCleanupPolicyAxis {
  if (draft === null) {
    return value;
  }
  const raw = draft.trim();
  if (raw === "") {
    return {
      ...value,
      cleanup_drive_schedule_enabled: true,
      cleanup_drive_schedule_interval_seconds: DEFAULT_FAILED_IMPORT_CLEANUP_RUN_INTERVAL_SECONDS,
    };
  }
  const minutes = Number(raw);
  if (!Number.isFinite(minutes)) {
    return {
      ...value,
      cleanup_drive_schedule_enabled: true,
      cleanup_drive_schedule_interval_seconds: DEFAULT_FAILED_IMPORT_CLEANUP_RUN_INTERVAL_SECONDS,
    };
  }
  const m = Math.min(Math.max(Math.trunc(minutes), RUN_INTERVAL_MIN_MINUTES), RUN_INTERVAL_MAX_MINUTES);
  if (m <= 0) {
    return { ...value, cleanup_drive_schedule_enabled: false, cleanup_drive_schedule_interval_seconds: 3600 };
  }
  const seconds = clampCleanupIntervalSeconds(m * 60);
  return { ...value, cleanup_drive_schedule_enabled: true, cleanup_drive_schedule_interval_seconds: seconds };
}

function describeSavedRunInterval(axis: FailedImportCleanupPolicyAxis): string {
  if (!axis.cleanup_drive_schedule_enabled) {
    return "Timed queue passes: off";
  }
  const sec = axis.cleanup_drive_schedule_interval_seconds;
  if (sec >= 3600 && sec % 3600 === 0) {
    const h = sec / 3600;
    return `Timed queue passes: every ${h} hour${h === 1 ? "" : "s"}`;
  }
  if (sec >= 60 && sec % 60 === 0) {
    const m = sec / 60;
    return `Timed queue passes: every ${m} minute${m === 1 ? "" : "s"}`;
  }
  const approxMin = Math.max(1, Math.round(sec / 60));
  return `Timed queue passes: about every ${approxMin} minutes`;
}

type PolicyClassRow = {
  field: HandlingField;
  primary: string;
  support: string;
};

function moviesClassRows(): PolicyClassRow[] {
  return [
    { field: "handling_quality_rejection", ...FETCHER_FI_POLICY_ROW_QUALITY },
    { field: "handling_unmatched_manual_import", ...FETCHER_FI_POLICY_ROW_MANUAL_IMPORT },
    { field: "handling_sample_release", ...FETCHER_FI_POLICY_ROW_SAMPLE },
    { field: "handling_corrupt_import", ...FETCHER_FI_POLICY_ROW_CORRUPT },
    { field: "handling_failed_download", ...FETCHER_FI_POLICY_ROW_DOWNLOAD_FAILED },
    { field: "handling_failed_import", ...FETCHER_FI_POLICY_ROW_GENERIC_IMPORT },
  ];
}

function tvClassRows(): PolicyClassRow[] {
  return [
    { field: "handling_quality_rejection", ...FETCHER_FI_POLICY_ROW_QUALITY },
    { field: "handling_unmatched_manual_import", ...FETCHER_FI_POLICY_ROW_MANUAL_IMPORT },
    { field: "handling_sample_release", ...FETCHER_FI_POLICY_ROW_SAMPLE },
    { field: "handling_corrupt_import", ...FETCHER_FI_POLICY_ROW_CORRUPT },
    { field: "handling_failed_download", ...FETCHER_FI_POLICY_ROW_DOWNLOAD_FAILED },
    { field: "handling_failed_import", ...FETCHER_FI_POLICY_ROW_GENERIC_IMPORT },
  ];
}

function cloneAxis(a: FailedImportCleanupPolicyAxis): FailedImportCleanupPolicyAxis {
  return { ...a };
}

function countNonLeaveAlone(axis: FailedImportCleanupPolicyAxis): number {
  return HANDLING_FIELDS.reduce((n, field) => n + (axis[field] !== "leave_alone" ? 1 : 0), 0);
}

function queueActionsSetHeadline(axis: FailedImportCleanupPolicyAxis): string {
  const n = countNonLeaveAlone(axis);
  if (n === 0) {
    return FETCHER_FI_POLICY_NO_OPTIONS_ON;
  }
  if (n === 1) {
    return "1 action enabled";
  }
  return `${n} actions enabled`;
}

function labelForQueueAction(value: FailedImportQueueHandlingAction): string {
  return FETCHER_FI_ACTION_OPTIONS.find((o) => o.value === value)?.label ?? value;
}

/** Compact current-action control; opens an anchored listbox popover (same four values as before). */
function FailedImportQueueActionPicker({
  groupId,
  field,
  primaryLabelId,
  value,
  disabled,
  onPick,
}: {
  groupId: string;
  field: HandlingField;
  primaryLabelId: string;
  value: FailedImportQueueHandlingAction;
  disabled: boolean;
  onPick: (next: FailedImportQueueHandlingAction) => void;
}) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const listboxId = `${groupId}-${field}-action-listbox`;

  useEffect(() => {
    if (!open) {
      return;
    }
    const onDocMouseDown = (e: MouseEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        setOpen(false);
      }
    };
    document.addEventListener("mousedown", onDocMouseDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onDocMouseDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  const currentLabel = labelForQueueAction(value);

  const chipBase =
    "inline-flex min-h-[2rem] max-w-[11rem] min-w-[8.25rem] items-center rounded-md border px-3 py-1.5 text-left text-sm font-semibold leading-snug tracking-normal transition-colors duration-150";

  if (disabled) {
    return (
      <div
        className={`${chipBase} cursor-default border-[var(--mm-border)]/50 bg-transparent text-[var(--mm-text2)] opacity-90`}
        data-testid={`${groupId}-${field}-action-picker-readonly`}
      >
        <span className="min-w-0 truncate">{currentLabel}</span>
      </div>
    );
  }

  return (
    <div ref={rootRef} className="relative shrink-0" data-testid={`${groupId}-${field}-action-picker`}>
      <button
        type="button"
        className={[
          chipBase,
          "cursor-pointer border-[var(--mm-border)]/70 bg-[var(--mm-card-bg)]/30 text-[var(--mm-text1)]",
          "hover:border-[rgba(212,175,55,0.38)] hover:bg-[var(--mm-accent-soft)]/18",
          open ? "border-[rgba(212,175,55,0.45)] bg-[var(--mm-accent-soft)]/22" : "",
          "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--mm-accent-ring)] focus-visible:ring-offset-2 focus-visible:ring-offset-[var(--mm-surface2)]",
        ].join(" ")}
        aria-expanded={open}
        aria-haspopup="listbox"
        aria-controls={open ? listboxId : undefined}
        aria-labelledby={primaryLabelId}
        onClick={() => setOpen((o) => !o)}
      >
        <span className="min-w-0 truncate">{currentLabel}</span>
      </button>
      {open ? (
        <div
          id={listboxId}
          role="listbox"
          aria-labelledby={primaryLabelId}
          className="absolute right-0 top-full z-[70] mt-1 min-w-[11rem] overflow-hidden rounded-md border border-[var(--mm-border)] bg-[var(--mm-card-bg)] py-1 shadow-md"
        >
          {FETCHER_FI_ACTION_OPTIONS.map((o) => {
            const selected = value === o.value;
            return (
              <button
                key={o.value}
                type="button"
                role="option"
                aria-selected={selected}
                className={[
                  "flex w-full cursor-pointer items-center px-3 py-2 text-left text-sm font-medium leading-snug transition-colors duration-100",
                  selected
                    ? "bg-[var(--mm-accent-soft)]/45 text-[var(--mm-text1)] hover:bg-[var(--mm-accent-soft)]/55"
                    : "text-[var(--mm-text2)] hover:bg-[rgba(212,175,55,0.14)] hover:text-[var(--mm-text1)] active:bg-[rgba(212,175,55,0.2)]",
                  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[var(--mm-accent-ring)]",
                ].join(" ")}
                onClick={() => {
                  onPick(o.value);
                  setOpen(false);
                }}
              >
                {o.label}
              </button>
            );
          })}
        </div>
      ) : null}
    </div>
  );
}

type CleanupAxis = "tv" | "movies";

function CleanupAxisCard({
  title,
  classRows,
  value,
  onChange,
  runIntervalDraft,
  setRunIntervalDraft,
  optionsDisabled,
  optionsGroupId,
  canEdit,
  saveLabel,
  saveTestId,
  savedTestId,
  onSave,
  saveDisabled,
  savePending,
  showSaved,
  showError,
  errorMessage,
}: {
  title: string;
  classRows: PolicyClassRow[];
  value: FailedImportCleanupPolicyAxis;
  onChange: (next: FailedImportCleanupPolicyAxis) => void;
  runIntervalDraft: string | null;
  setRunIntervalDraft: (next: string | null) => void;
  optionsDisabled: boolean;
  optionsGroupId: string;
  canEdit: boolean;
  saveLabel: string;
  saveTestId: string;
  savedTestId: string;
  onSave: () => void;
  saveDisabled: boolean;
  savePending: boolean;
  showSaved: boolean;
  showError: boolean;
  errorMessage: string;
}) {
  const runIntervalInputValue =
    runIntervalDraft !== null ? runIntervalDraft : committedCleanupRunIntervalMinutesText(value);

  const commitRunIntervalFromDraft = () => {
    if (runIntervalDraft === null) {
      return;
    }
    const next = finalizeFailedImportCleanupRunIntervalDraft(runIntervalDraft, value);
    setRunIntervalDraft(null);
    if (
      next.cleanup_drive_schedule_enabled !== value.cleanup_drive_schedule_enabled ||
      next.cleanup_drive_schedule_interval_seconds !== value.cleanup_drive_schedule_interval_seconds
    ) {
      onChange(next);
    }
  };

  return (
    <div
      className={[
        "flex h-full flex-col rounded-lg border border-[var(--mm-border)] bg-[var(--mm-surface2)]/25 p-5 shadow-sm transition-shadow duration-200",
        showSaved
          ? "ring-2 ring-[var(--mm-accent-ring)] ring-offset-2 ring-offset-[var(--mm-bg-main)] shadow-[0_0_0_1px_rgba(212,175,55,0.12)]"
          : "",
      ].join(" ")}
    >
      <h3 className="text-sm font-semibold text-[var(--mm-text1)]">{title}</h3>
      <p className="mt-2 text-sm leading-snug text-[var(--mm-text2)]">{queueActionsSetHeadline(value)}</p>

      <div className="mt-5 space-y-1.5">
        <p className="text-sm font-semibold text-[var(--mm-text1)]">{FETCHER_FI_POLICY_QUEUE_ACTIONS_SUBHEADING}</p>
        <p className="max-w-[40rem] text-[11px] leading-relaxed text-[var(--mm-text3)]/80">{FETCHER_FI_POLICY_ACTION_LEGEND}</p>
        <p id={optionsGroupId} className="text-[11px] leading-snug text-[var(--mm-text3)]/75">
          {FETCHER_FI_POLICY_OPTIONS_GROUP_LABEL}
        </p>
        <ul className="mt-3 divide-y divide-[var(--mm-border)]/80" data-testid={`${optionsGroupId}-rows`}>
          {classRows.map(({ field, primary, support }) => {
            const primaryLabelId = `${optionsGroupId}-${field}-primary-label`;
            return (
              <li
                key={field}
                className="flex flex-col gap-2.5 py-3.5 first:pt-2.5 sm:flex-row sm:items-start sm:justify-between sm:gap-x-6 sm:gap-y-0 sm:py-3.5"
              >
                <div className="min-w-0 flex-1 sm:pr-1">
                  <span id={primaryLabelId} className="block text-sm font-medium leading-snug text-[var(--mm-text1)]">
                    {primary}
                  </span>
                  <p className="mt-1 text-[11px] font-normal leading-relaxed text-[var(--mm-text3)]/78">{support}</p>
                </div>
                <div className="shrink-0 sm:pt-px">
                  <FailedImportQueueActionPicker
                    groupId={optionsGroupId}
                    field={field}
                    primaryLabelId={primaryLabelId}
                    value={value[field]}
                    disabled={optionsDisabled}
                    onPick={(next) =>
                      onChange({
                        ...value,
                        [field]: next,
                      })
                    }
                  />
                </div>
              </li>
            );
          })}
        </ul>
      </div>

      <div className="mt-6 space-y-2 rounded-md border border-[var(--mm-border)] bg-[var(--mm-bg-main)]/20 p-4">
        <p className="text-sm font-semibold text-[var(--mm-text1)]">{FETCHER_FI_POLICY_AUTOMATIC_CHECK_SUBHEADING}</p>
        <span className="block text-sm font-medium text-[var(--mm-text2)]">{FETCHER_FI_POLICY_RUN_INTERVAL_LABEL}</span>
        <p className="text-xs leading-snug text-[var(--mm-text3)]">{FETCHER_FI_POLICY_RUN_INTERVAL_HELPER}</p>
        {canEdit ? (
          <input
            id={`${optionsGroupId}-run-interval`}
            type="number"
            min={RUN_INTERVAL_MIN_MINUTES}
            max={RUN_INTERVAL_MAX_MINUTES}
            className="mm-input mt-1 w-full"
            disabled={optionsDisabled}
            data-testid={`${optionsGroupId}-run-interval`}
            value={runIntervalInputValue}
            onFocus={() => setRunIntervalDraft(committedCleanupRunIntervalMinutesText(value))}
            onChange={(e) => setRunIntervalDraft(e.target.value)}
            onBlur={() => commitRunIntervalFromDraft()}
          />
        ) : (
          <p
            id={`${optionsGroupId}-run-interval`}
            className="mt-1 text-sm text-[var(--mm-text2)]"
            data-testid={`${optionsGroupId}-run-interval-readonly`}
          >
            {describeSavedRunInterval(value)}
          </p>
        )}
      </div>

      {canEdit ? (
        <div className="mt-6 space-y-4 border-t border-[var(--mm-border)] pt-5">
          {showSaved ? (
            <p
              className="rounded-md border border-[rgba(212,175,55,0.45)] bg-[var(--mm-accent-soft)] px-3 py-2 text-sm font-medium text-[var(--mm-text1)]"
              role="status"
              data-testid={savedTestId}
            >
              Saved.
            </p>
          ) : null}
          {showError ? (
            <p className="text-sm text-red-400" role="alert">
              {errorMessage}
            </p>
          ) : null}
          <button
            type="button"
            data-testid={saveTestId}
            className={fetcherMenuButtonClass({
              variant: "primary",
              disabled: saveDisabled,
            })}
            disabled={saveDisabled}
            onClick={onSave}
          >
            {savePending ? FETCHER_FI_POLICY_SAVING : saveLabel}
          </button>
          <p className="text-xs leading-relaxed text-[var(--mm-text3)]">{FETCHER_FI_POLICY_CARD_LEAD_SECOND}</p>
        </div>
      ) : null}
    </div>
  );
}

export type FetcherFailedImportsCleanupPolicyAxes = "both" | "tv" | "movies";

/** Fetcher failed-imports policy: per-class Sonarr/Radarr queue actions with per-axis save. */
export function FetcherFailedImportsCleanupPolicySection({
  role,
  axes = "both",
}: {
  role: string | undefined;
  axes?: FetcherFailedImportsCleanupPolicyAxes;
}) {
  const q = useFailedImportCleanupPolicyQuery();
  const saveTv = useFailedImportCleanupPolicySaveTvMutation();
  const saveMovies = useFailedImportCleanupPolicySaveMoviesMutation();
  const canEdit = showFailedImportCleanupPolicyEditor(role);

  const [movies, setMovies] = useState<FailedImportCleanupPolicyAxis | null>(null);
  const [tvShows, setTvShows] = useState<FailedImportCleanupPolicyAxis | null>(null);
  const [tvRunIntervalDraft, setTvRunIntervalDraft] = useState<string | null>(null);
  const [moviesRunIntervalDraft, setMoviesRunIntervalDraft] = useState<string | null>(null);
  const [savedTvFlash, setSavedTvFlash] = useState(false);
  const [savedMoviesFlash, setSavedMoviesFlash] = useState(false);
  const [errorAxis, setErrorAxis] = useState<CleanupAxis | null>(null);

  useEffect(() => {
    if (q.data) {
      setMovies(cloneAxis(q.data.movies));
      setTvShows(cloneAxis(q.data.tv_shows));
      setTvRunIntervalDraft(null);
      setMoviesRunIntervalDraft(null);
    }
  }, [q.data]);

  const dirtyTv = Boolean(
    q.data &&
      tvShows &&
      (JSON.stringify(tvShows) !== JSON.stringify(q.data.tv_shows) ||
        draftDiffersFromCommittedLabel(tvRunIntervalDraft, committedCleanupRunIntervalMinutesText(tvShows))),
  );
  const dirtyMovies = Boolean(
    q.data &&
      movies &&
      (JSON.stringify(movies) !== JSON.stringify(q.data.movies) ||
        draftDiffersFromCommittedLabel(moviesRunIntervalDraft, committedCleanupRunIntervalMinutesText(movies))),
  );

  useEffect(() => {
    if (dirtyTv) {
      setSavedTvFlash(false);
    }
  }, [dirtyTv]);

  useEffect(() => {
    if (dirtyMovies) {
      setSavedMoviesFlash(false);
    }
  }, [dirtyMovies]);

  useEffect(() => {
    if (!savedTvFlash) {
      return;
    }
    const t = window.setTimeout(() => setSavedTvFlash(false), 2400);
    return () => window.clearTimeout(t);
  }, [savedTvFlash]);

  useEffect(() => {
    if (!savedMoviesFlash) {
      return;
    }
    const t = window.setTimeout(() => setSavedMoviesFlash(false), 2400);
    return () => window.clearTimeout(t);
  }, [savedMoviesFlash]);

  const mutateSave = (axis: CleanupAxis) => {
    if (!movies || !tvShows || !q.data) {
      return;
    }
    let nextMovies = movies;
    let nextTv = tvShows;
    if (axis === "tv") {
      nextTv = finalizeFailedImportCleanupRunIntervalDraft(tvRunIntervalDraft, tvShows);
      setTvRunIntervalDraft(null);
      setTvShows(nextTv);
    } else {
      nextMovies = finalizeFailedImportCleanupRunIntervalDraft(moviesRunIntervalDraft, movies);
      setMoviesRunIntervalDraft(null);
      setMovies(nextMovies);
    }
    setErrorAxis(null);
    const mut = axis === "tv" ? saveTv : saveMovies;
    const payload = axis === "tv" ? nextTv : nextMovies;
    mut.mutate(payload, {
      onSuccess: () => {
        if (axis === "tv") {
          setSavedTvFlash(true);
        } else {
          setSavedMoviesFlash(true);
        }
      },
      onError: () => {
        setErrorAxis(axis);
      },
    });
  };

  const saveTvErrorMessage = saveTv.error instanceof Error ? saveTv.error.message : "Save failed.";
  const saveMoviesErrorMessage = saveMovies.error instanceof Error ? saveMovies.error.message : "Save failed.";

  return (
    <section
      className="mm-card mm-dash-card mm-fetcher-module-surface"
      aria-labelledby="mm-fetcher-fi-policy-heading"
      data-testid="fetcher-failed-imports-cleanup-policy"
    >
      <h2 id="mm-fetcher-fi-policy-heading" className="mm-card__title">
        {FETCHER_FI_POLICY_CARD_TITLE}
      </h2>

      {q.isPending ? (
        <p
          className="mm-card__body mm-card__body--tight mt-1 text-sm text-[var(--mm-text3)]"
          data-testid="fetcher-failed-imports-policy-loading"
        >
          Loading queue action settings…
        </p>
      ) : q.isError ? (
        <p
          className="mm-card__body mm-card__body--tight mt-1 text-sm text-red-400"
          data-testid="fetcher-failed-imports-policy-error"
          role="alert"
        >
          {q.error instanceof Error ? q.error.message : "Could not load queue action settings."}
        </p>
      ) : q.data && movies && tvShows ? (
        <div className="mm-card__body mm-card__body--tight mt-1">
          <p className="mb-4 text-sm text-[var(--mm-text2)]">{FETCHER_FI_POLICY_CARD_LEAD}</p>
          <div
            className={axes === "both" ? "grid gap-4 sm:grid-cols-2 sm:gap-5" : "grid max-w-3xl gap-4 sm:gap-5"}
          >
            {axes !== "movies" ? (
              <CleanupAxisCard
                title={FETCHER_FI_POLICY_TV_HEADING}
                classRows={tvClassRows()}
                value={tvShows}
                onChange={setTvShows}
                runIntervalDraft={tvRunIntervalDraft}
                setRunIntervalDraft={setTvRunIntervalDraft}
                optionsDisabled={!canEdit || saveTv.isPending}
                optionsGroupId="mm-fi-cleanup-tv-options"
                canEdit={canEdit}
                saveLabel={FETCHER_FI_POLICY_SAVE_SONARR}
                saveTestId="fetcher-failed-imports-policy-save-tv"
                savedTestId="fetcher-failed-imports-policy-saved-tv"
                onSave={() => mutateSave("tv")}
                saveDisabled={!dirtyTv || saveTv.isPending}
                savePending={saveTv.isPending}
                showSaved={savedTvFlash && !dirtyTv && !saveTv.isPending}
                showError={Boolean(saveTv.isError && errorAxis === "tv")}
                errorMessage={saveTvErrorMessage}
              />
            ) : null}
            {axes !== "tv" ? (
              <CleanupAxisCard
                title={FETCHER_FI_POLICY_MOVIES_HEADING}
                classRows={moviesClassRows()}
                value={movies}
                onChange={setMovies}
                runIntervalDraft={moviesRunIntervalDraft}
                setRunIntervalDraft={setMoviesRunIntervalDraft}
                optionsDisabled={!canEdit || saveMovies.isPending}
                optionsGroupId="mm-fi-cleanup-movies-options"
                canEdit={canEdit}
                saveLabel={FETCHER_FI_POLICY_SAVE_RADARR}
                saveTestId="fetcher-failed-imports-policy-save-movies"
                savedTestId="fetcher-failed-imports-policy-saved-movies"
                onSave={() => mutateSave("movies")}
                saveDisabled={!dirtyMovies || saveMovies.isPending}
                savePending={saveMovies.isPending}
                showSaved={savedMoviesFlash && !dirtyMovies && !saveMovies.isPending}
                showError={Boolean(saveMovies.isError && errorAxis === "movies")}
                errorMessage={saveMoviesErrorMessage}
              />
            ) : null}
          </div>
          {!canEdit ? (
            <p className="mt-4 text-sm text-[var(--mm-text3)]">{FETCHER_FI_POLICY_VIEWER_NOTE}</p>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}
