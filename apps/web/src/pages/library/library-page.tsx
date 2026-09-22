/**
 * Library — the files already on your storage, and what Weir would do to each (library mode, #505/#568).
 * The second of Weir's two jobs: not the hand-off with a media manager, but the shelves themselves.
 *
 * The title is the picker (James, 22 Sep 2026): "Library › TV ▾", so one library needs no chooser at all and
 * many need no second menu. Under it, one row of chips holds the numbers and the filters together — how many
 * files would change, how many already match, how many Weir will not touch — because a count nobody can act
 * on is decoration. The table groups a library the way a person thinks of it, by title and season, and a file
 * opens a panel that says track by track what would go and why (docs/exec-plans/active/live-and-library.md).
 *
 * Every number here comes from the library scan on the server; nothing is counted in the browser.
 */
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { PageHeader } from "../../components/shell/page-header";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import { ApiEntryError } from "../../components/shared/api-entry-error";
import { PageLoading } from "../../components/shared/page-loading";
import { formatBytes } from "../../lib/format/bytes";
import { useActivityStreamInvalidations } from "../../lib/activity/use-activity-stream-invalidation";
import {
  type LibraryCleanResult,
  type LibraryFile,
  type LibraryFileClassification,
  type LibraryFileFilters,
  type LibraryProblemKind,
} from "../../lib/processing/library-api";
import {
  useCleanLibraryFiles,
  useLibraryFilesQuery,
  useLibraryOverviewQuery,
  useSetLibraryFileLeaveAlone,
  useTriggerLibraryScan,
} from "../../lib/processing/library-queries";
import { useProcessingLibrariesQuery } from "../../lib/processing/libraries-queries";
import { LibraryFileDrawer } from "./library-file-drawer";
import { LibraryPicker } from "./library-picker";

const PAGE_SIZE = 200;

/** Compact rows are this browser's choice, like a zoom level: nothing about the library changes. */
const COMPACT_KEY = "weir-library-compact";

function readCompact(): boolean {
  try {
    return localStorage.getItem(COMPACT_KEY) === "1";
  } catch {
    return false;
  }
}

/** The chips: a count and the filter it stands for, in the order an operator reads them. */
const CHIPS: { id: LibraryFileClassification; label: string; hint: string }[] =
  [
    {
      id: "would_change",
      label: "Would change",
      hint: "Weir would take tracks out of these",
    },
    {
      id: "matches",
      label: "Matches your rules",
      hint: "Nothing to do: these already look the way your rules ask",
    },
    {
      id: "cannot_process",
      label: "Weir will not touch",
      hint: "Still seeding, or Weir cannot read them",
    },
  ];

/** The same row of chips, for what Weir has done with a file rather than what is in it. */
const STATE_CHIPS: {
  id: "cleaned" | "left_alone";
  label: string;
  hint: string;
}[] = [
  {
    id: "cleaned",
    label: "Cleaned",
    hint: "Weir has cleaned these at least once; a rescan does not forget",
  },
  {
    id: "left_alone",
    label: "Left alone",
    hint: "You set these aside; nothing cleans them until you say otherwise",
  },
];

const PROBLEM_LABELS: Record<LibraryProblemKind, string> = {
  seeding: "Still seeding",
  manager_redownload: "Your manager would download it again",
  no_permission: "Weir cannot write to it",
  unreadable: "Weir cannot read it",
  no_video: "No video in it",
  no_audio_left: "The rules would leave no audio",
};

/**
 * What a file belongs under: the title its media manager knows it by, or, with no manager, the folders it sits
 * in. A season folder on its own says nothing, so it is shown with the show above it.
 */
function groupOf(file: LibraryFile): string {
  if (file.manager_title) return file.manager_title;
  const parts = file.path.split(/[\\/]/).filter(Boolean);
  const parent = parts.at(-2);
  const above = parts.at(-3);
  if (parent && above && /^(season|series)\s*\d+$/i.test(parent)) {
    return `${above} · ${parent}`;
  }
  return parent ?? parts[0] ?? "Files";
}

/** The table says what would happen in as few words as fit the column; the panel carries the whole sentence. */
function verdictOf(file: LibraryFile): string {
  if (file.classification === "matches") return "Matches the rules";
  if (file.classification === "cannot_process") {
    const text = (
      file.reason ??
      file.summary ??
      "Weir will not touch this one"
    ).trim();
    const end = text.search(/[.!?](\s|$)/);
    return end > 0 ? text.slice(0, end) : text;
  }

  const parts: string[] = [];
  if (file.removed_audio_tracks)
    parts.push(`${file.removed_audio_tracks} audio`);
  if (file.removed_subtitle_tracks) {
    parts.push(
      `${file.removed_subtitle_tracks} ${file.removed_subtitle_tracks === 1 ? "subtitle" : "subtitles"}`,
    );
  }
  return parts.length ? `Removes ${parts.join(", ")}` : "Would change";
}

function fileName(path: string): string {
  return path.split(/[\\/]/).filter(Boolean).at(-1) ?? path;
}

/** How long ago the scan that these numbers come from ran. */
function scanned(generatedAt: number | null, now: number): string {
  if (!generatedAt) return "not scanned yet";
  const minutes = Math.max(0, Math.round((now - generatedAt * 1000) / 60_000));
  if (minutes < 1) return "checked just now";
  if (minutes < 90) return `checked ${minutes} min ago`;
  const hours = Math.round(minutes / 60);
  return hours < 36
    ? `checked ${hours} h ago`
    : `checked ${Math.round(hours / 24)} days ago`;
}

export function LibraryPage(): React.ReactElement {
  const libraries = useProcessingLibrariesQuery();
  const [params, setParams] = useSearchParams();
  const [libraryId, setLibraryId] = useState<number | null>(null);
  const [search, setSearch] = useState(params.get("q") ?? "");
  const [classification, setClassification] =
    useState<LibraryFileClassification | null>(null);
  const [problem, setProblem] = useState<LibraryProblemKind | null>(null);
  const [state, setState] = useState<"cleaned" | "left_alone" | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [openPath, setOpenPath] = useState<string | null>(params.get("path"));
  const [confirming, setConfirming] = useState<string | null>(null);
  const [outcome, setOutcome] = useState<LibraryCleanResult | null>(null);
  const [compact, setCompact] = useState(readCompact);
  const [now] = useState(() => Date.now());
  const searchTimer = useRef<number | null>(null);
  const [query, setQuery] = useState(params.get("q") ?? "");

  // The chosen library survives a reload and a link, so "open this library" is a plain URL.
  const chosen = useMemo(() => {
    const list = libraries.data ?? [];
    const wanted = libraryId ?? Number(params.get("library") ?? 0);
    return list.find((l) => l.id === wanted) ?? list[0] ?? null;
  }, [libraries.data, libraryId, params]);

  useEffect(() => {
    // Typing narrows the table, but not on every keystroke: the server reads the whole index for each search.
    if (searchTimer.current !== null) window.clearTimeout(searchTimer.current);
    searchTimer.current = window.setTimeout(() => setQuery(search.trim()), 300);
    return () => {
      if (searchTimer.current !== null)
        window.clearTimeout(searchTimer.current);
    };
  }, [search]);

  const filters: LibraryFileFilters = useMemo(
    () => ({
      ...(classification ? { classification } : {}),
      ...(problem ? { problem } : {}),
      ...(state ? { state } : {}),
      ...(query ? { q: query } : {}),
      sort: "path",
      direction: "asc",
      page: 1,
      page_size: PAGE_SIZE,
    }),
    [classification, problem, state, query],
  );

  // A clean finishes on the server, minutes after the button was pressed, and that is when "Cleaned" becomes
  // true of a file. Slowly, because a library is a long list to re-read and nothing here changes by the second.
  const libraryKeys = useMemo(
    () => [
      ["processing", "library-files", chosen?.id ?? 0],
      ["processing", "library-overview", chosen?.id ?? 0],
    ],
    [chosen?.id],
  );
  useActivityStreamInvalidations(libraryKeys, { throttleMs: 5_000 });

  const overview = useLibraryOverviewQuery(chosen?.id ?? 0, Boolean(chosen));
  const files = useLibraryFilesQuery(chosen?.id ?? 0, filters, Boolean(chosen));
  const clean = useCleanLibraryFiles(chosen?.id ?? 0);
  const leaveAlone = useSetLibraryFileLeaveAlone(chosen?.id ?? 0);
  const rescan = useTriggerLibraryScan(chosen?.id ?? 0);

  const pick = useCallback(
    (id: number) => {
      setLibraryId(id);
      setSelected(new Set());
      setOpenPath(null);
      const next = new URLSearchParams(params);
      next.set("library", String(id));
      setParams(next, { replace: true });
    },
    [params, setParams],
  );

  const toggle = useCallback((path: string) => {
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
  }, []);

  const groups = useMemo(() => {
    const byTitle = new Map<string, LibraryFile[]>();
    for (const file of files.data?.files ?? []) {
      const key = groupOf(file);
      const list = byTitle.get(key);
      if (list) list.push(file);
      else byTitle.set(key, [file]);
    }
    return [...byTitle.entries()].sort((a, b) => a[0].localeCompare(b[0]));
  }, [files.data]);

  if (libraries.isPending)
    return <PageLoading label="Loading your libraries" />;
  if (libraries.isError) {
    return (
      <div className="mm-page">
        <ApiEntryError error={libraries.error} />
      </div>
    );
  }

  const totals = overview.data?.totals;
  const scan = overview.data?.scan ?? files.data?.scan ?? null;
  const shown = files.data?.filtered;
  const problems = overview.data?.problems ?? [];
  const selectedFiles = (files.data?.files ?? []).filter((f) =>
    selected.has(f.path),
  );
  const selectedSaving = selectedFiles.reduce(
    (sum, f) => sum + f.estimated_bytes_saved,
    0,
  );

  if (!chosen) {
    return (
      <div className="mm-page mm-library" data-testid="library-page">
        <PageHeader
          title="Library"
          lead="The files already on your storage, and what Weir would do to each."
        />
        <p className="mm-library-empty">
          No library is set up yet. Add one in Settings › Libraries, give it the
          folders your media sits in, and Weir will check what is there.
        </p>
      </div>
    );
  }

  return (
    <div className="mm-page mm-library" data-testid="library-page">
      <PageHeader
        title="Library"
        titleAfter={
          <LibraryPicker
            libraries={libraries.data ?? []}
            chosenId={chosen.id}
            onPick={pick}
            countFor={(id) =>
              id === chosen.id ? (totals?.files ?? null) : null
            }
          />
        }
        lead={
          totals
            ? `${totals.files.toLocaleString()} files, ${formatBytes(totals.size_bytes)} on your storage. Weir reads them where they are and only changes one when you ask.`
            : "Weir reads your library where it is and only changes a file when you ask."
        }
        aside={
          <div className="mm-library-scan" data-testid="library-scan">
            {scan?.running ? (
              <>
                <i className="mm-live-pulse" aria-hidden="true" />
                <span>Checking this library now</span>
              </>
            ) : (
              <>
                <span className="mm-library-scan__dot" aria-hidden="true" />
                <span>{scanned(scan?.generated_at ?? null, now)}</span>
              </>
            )}
            <button
              type="button"
              className="mm-head-control"
              disabled={rescan.isPending || Boolean(scan?.running)}
              onClick={() => rescan.mutate()}
            >
              Check again
            </button>
          </div>
        }
      />

      <div className="mm-library-toolbar">
        <input
          type="search"
          className="mm-input mm-library-search"
          placeholder="Search this library"
          aria-label="Search this library"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
        />
        <div className="mm-library-chips" role="group" aria-label="Show">
          {CHIPS.map((chip) => {
            const count =
              chip.id === "would_change"
                ? totals?.would_change
                : chip.id === "matches"
                  ? totals?.matches
                  : totals?.cannot_process;
            return (
              <button
                key={chip.id}
                type="button"
                className="mm-library-chip"
                title={chip.hint}
                aria-pressed={classification === chip.id}
                onClick={() => {
                  setClassification(
                    classification === chip.id ? null : chip.id,
                  );
                  setProblem(null);
                  setState(null);
                }}
              >
                {chip.label} <b>{count?.toLocaleString() ?? "—"}</b>
              </button>
            );
          })}
          {STATE_CHIPS.map((chip) => (
            <button
              key={chip.id}
              type="button"
              className="mm-library-chip"
              title={chip.hint}
              aria-pressed={state === chip.id}
              onClick={() => {
                setState(state === chip.id ? null : chip.id);
                setClassification(null);
                setProblem(null);
              }}
            >
              {chip.label}{" "}
              <b>
                {(chip.id === "cleaned"
                  ? totals?.cleaned
                  : totals?.left_alone
                )?.toLocaleString() ?? "—"}
              </b>
            </button>
          ))}
          {problems.length > 0 ? (
            <label className="mm-library-problem">
              <span className="sr-only">Why Weir will not touch a file</span>
              <select
                className="mm-input"
                value={problem ?? ""}
                onChange={(event) => {
                  const value = event.target.value as LibraryProblemKind | "";
                  setProblem(value === "" ? null : value);
                  setClassification(null);
                }}
              >
                <option value="">Any reason</option>
                {problems.map((group) => (
                  <option key={group.kind} value={group.kind}>
                    {PROBLEM_LABELS[group.kind]} ({group.files.toLocaleString()}
                    )
                  </option>
                ))}
              </select>
            </label>
          ) : null}
        </div>
        <button
          type="button"
          className="mm-library-chip"
          aria-pressed={compact}
          title="Fit more files on the screen"
          onClick={() => {
            const next = !compact;
            setCompact(next);
            try {
              localStorage.setItem(COMPACT_KEY, next ? "1" : "0");
            } catch {
              // A browser that will not remember the choice is no reason to refuse it.
            }
          }}
        >
          Compact rows
        </button>
        <p className="mm-library-saving">
          {/* Never claim there is nothing to reclaim when Weir simply could not measure it: these files carry no
              per-track size for the scan to add up. */}
          {totals && totals.estimated_bytes_saved > 0
            ? `About ${formatBytes(totals.estimated_bytes_saved)} back if everything that would change is cleaned`
            : totals && totals.would_change > 0
              ? `${totals.total_removed_audio_tracks + totals.total_removed_subtitle_tracks} tracks would come out; these files do not say how big each one is`
              : "Nothing to reclaim here at the moment"}
        </p>
      </div>

      {selected.size > 0 ? (
        <div className="mm-library-bulk" data-testid="library-bulk">
          <span>
            <b>{selected.size.toLocaleString()}</b> selected
            {selectedSaving > 0
              ? ` · about ${formatBytes(selectedSaving)} back`
              : ""}
          </span>
          <button
            type="button"
            className={mmActionButtonClass({ variant: "primary" })}
            disabled={clean.isPending}
            onClick={() =>
              clean.mutate(
                { paths: [...selected], confirm: false },
                {
                  onSuccess: (result) => {
                    if (result.kind === "confirmation_required") {
                      setConfirming(result.detail);
                      return;
                    }

                    setSelected(new Set());
                    setOutcome(result);
                  },
                },
              )
            }
          >
            Clean these files
          </button>
          <button
            type="button"
            className="mm-head-control"
            onClick={() => setSelected(new Set())}
          >
            Clear
          </button>
        </div>
      ) : null}

      {confirming ? (
        <div
          className="mm-library-confirm"
          role="alertdialog"
          aria-label="Confirm cleaning"
          data-testid="library-confirm"
        >
          <p>{confirming}</p>
          <div className="mm-library-confirm__actions">
            <button
              type="button"
              className={mmActionButtonClass({ variant: "primary" })}
              disabled={clean.isPending}
              onClick={() =>
                clean.mutate(
                  { paths: [...selected], confirm: true },
                  {
                    onSuccess: (result) => {
                      setConfirming(null);
                      setSelected(new Set());
                      if (result.kind === "cleaned") setOutcome(result);
                    },
                  },
                )
              }
            >
              Yes, clean them
            </button>
            <button
              type="button"
              className="mm-head-control"
              onClick={() => setConfirming(null)}
            >
              Not now
            </button>
          </div>
        </div>
      ) : null}

      {outcome ? (
        <div className="mm-library-outcome" data-testid="library-outcome">
          <p>
            {outcome.queued > 0
              ? `${outcome.queued.toLocaleString()} ${outcome.queued === 1 ? "file is" : "files are"} queued to clean.`
              : "Nothing was queued."}
            {outcome.skipped_paths.length > 0
              ? ` Weir left ${outcome.skipped_paths.length.toLocaleString()} alone: ${outcome.skipped_paths
                  .map(fileName)
                  .join(", ")}.`
              : ""}
          </p>
          {outcome.warnings.map((warning) => (
            <p key={warning} className="mm-library-outcome__why">
              {warning}
            </p>
          ))}
          <button
            type="button"
            className="mm-head-control"
            onClick={() => setOutcome(null)}
          >
            Close
          </button>
        </div>
      ) : null}

      {clean.isError ? (
        <p className="mm-library-error">{clean.error.message}</p>
      ) : null}

      {files.isPending ? (
        <PageLoading label="Reading this library" />
      ) : files.isError ? (
        <ApiEntryError error={files.error} />
      ) : groups.length === 0 ? (
        <p className="mm-library-empty" data-testid="library-empty">
          {totals && totals.files > 0
            ? "Nothing here matches what you asked for. Clear the filters to see the whole library."
            : "This library has not been scanned yet, or its folders hold nothing Weir reads. Check again, or set its folders in Settings › Libraries."}
        </p>
      ) : (
        <div
          className={`mm-library-table${compact ? " mm-library-table--compact" : ""}`}
          role="table"
          aria-label={`Files in ${chosen.name}`}
        >
          <div role="row" className="mm-library-row mm-library-row--head">
            <span role="columnheader" className="sr-only">
              Select
            </span>
            <span role="columnheader">File</span>
            <span role="columnheader">What Weir would do</span>
            <span role="columnheader">Audio</span>
            <span role="columnheader">Subtitles</span>
            <span role="columnheader" className="mm-library-num">
              Size
            </span>
            <span role="columnheader" className="mm-library-num">
              Back
            </span>
          </div>
          {groups.map(([title, rows]) => (
            <div key={title} role="rowgroup" className="mm-library-group">
              <div role="row" className="mm-library-grouprow">
                <span role="cell" className="mm-library-grouprow__title">
                  {title}
                </span>
                <span role="cell" className="mm-library-grouprow__meta">
                  {rows[0]?.manager_kind
                    ? `${rows[0].manager_kind === "sonarr" ? "Sonarr" : "Radarr"} · `
                    : ""}
                  {rows.length === 1 ? "1 file" : `${rows.length} files`}
                  {rows.some((r) => r.classification === "would_change")
                    ? ` · ${rows.filter((r) => r.classification === "would_change").length} would change`
                    : ""}
                </span>
              </div>
              {rows.map((file) => (
                <div
                  key={file.path}
                  role="row"
                  className={`mm-library-row mm-library-row--${file.classification}${
                    openPath === file.path ? " mm-library-row--open" : ""
                  }`}
                  data-testid="library-row"
                >
                  <span role="cell">
                    <input
                      type="checkbox"
                      className="mm-library-check"
                      aria-label={`Select ${fileName(file.path)}`}
                      checked={selected.has(file.path)}
                      disabled={file.classification !== "would_change"}
                      onChange={() => toggle(file.path)}
                    />
                  </span>
                  <span role="cell" className="mm-library-name">
                    <button
                      type="button"
                      onClick={() => setOpenPath(file.path)}
                    >
                      {fileName(file.path)}
                    </button>
                  </span>
                  <span role="cell" className="mm-library-verdict">
                    {verdictOf(file)}
                  </span>
                  <span role="cell" className="mm-library-tracks">
                    {file.audio_summary ?? `${file.audio_track_count}`}
                  </span>
                  <span role="cell" className="mm-library-tracks">
                    {file.subtitle_summary ?? `${file.subtitle_track_count}`}
                  </span>
                  <span role="cell" className="mm-library-num">
                    {formatBytes(file.size_bytes)}
                  </span>
                  <span role="cell" className="mm-library-num">
                    {file.estimated_bytes_saved > 0
                      ? formatBytes(file.estimated_bytes_saved)
                      : "—"}
                  </span>
                </div>
              ))}
            </div>
          ))}
          {files.data && files.data.total > (shown?.files ?? 0) ? (
            <p className="mm-library-more">
              Showing {(shown?.files ?? 0).toLocaleString()} of{" "}
              {files.data.total.toLocaleString()}. Narrow it with search or a
              chip to see the rest.
            </p>
          ) : null}
        </div>
      )}

      <LibraryFileDrawer
        libraryId={chosen.id}
        libraryName={chosen.name}
        file={
          (files.data?.files ?? []).find((f) => f.path === openPath) ?? null
        }
        onClose={() => setOpenPath(null)}
        onClean={(path, manual) =>
          clean.mutate(
            { paths: [path], confirm: true, manual },
            { onSuccess: () => setOpenPath(null) },
          )
        }
        onLeaveAlone={(path, value) =>
          leaveAlone.mutate({ path, leaveAlone: value })
        }
        cleaning={clean.isPending}
        settingAside={leaveAlone.isPending}
      />
    </div>
  );
}
