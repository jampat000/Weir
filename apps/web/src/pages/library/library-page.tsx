/**
 * Library: the files already on your storage, and what Weir would do to each (library mode, #505/#568).
 * The second of Weir's two jobs: not the hand-off with a media manager, but the shelves themselves.
 *
 * The title is the picker, "Library › TV ▾", so one library needs no chooser at all and many need no
 * second menu. Under it, one row of chips holds the numbers and the filters together, because a count
 * nobody can act on is decoration. The table groups a library the way a person thinks of it, by title
 * and season, and a file opens a panel that says track by track what would go and why.
 *
 * Every number here comes from the library scan on the server; nothing is counted in the browser.
 */
import { useCallback, useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";

import { ApiEntryError } from "../../components/shared/api-entry-error";
import { PageLoading } from "../../components/shared/page-loading";
import { PageHeader } from "../../components/shell/page-header";
import { useActivityStreamInvalidations } from "../../lib/activity/use-activity-stream-invalidation";
import { formatBytes } from "../../lib/format/bytes";
import type { LibraryFileFilters } from "../../lib/processing/library-mode-api";
import {
  useCleanLibraryFiles,
  useLibraryFilesQuery,
  useLibraryOverviewQuery,
  useSetLibraryFileLeaveAlone,
} from "../../lib/processing/library-mode-queries";
import { useProcessingLibrariesQuery } from "../../lib/processing/libraries-queries";
import { processingKeys } from "../../lib/processing/query-keys";
import { useDebouncedValue } from "../../lib/ui/use-debounced-value";
import { LibraryCleanActions } from "./library-clean-actions";
import { LibraryFileDrawer } from "./library-file-drawer";
import { groupFiles } from "./library-model";
import { LibraryPicker } from "./library-picker";
import { LibraryScanStatus } from "./library-scan-status";
import { LibraryTable } from "./library-table";
import {
  LibraryToolbar,
  NO_FILTER,
  type LibraryFilter,
} from "./library-toolbar";

const PAGE_SIZE = 200;
/** Typing narrows the table, but not on every keystroke: the server reads the whole index for each search. */
const SEARCH_DEBOUNCE_MS = 300;
/** A clean finishes on the server minutes after the button, and a library is a long list to re-read. */
const REFRESH_THROTTLE_MS = 5_000;

/** Compact rows are this browser's choice, like a zoom level: nothing about the library changes. */
const COMPACT_KEY = "weir-library-compact";

function readCompact(): boolean {
  try {
    return localStorage.getItem(COMPACT_KEY) === "1";
  } catch {
    return false;
  }
}

function saveCompact(compact: boolean): void {
  try {
    localStorage.setItem(COMPACT_KEY, compact ? "1" : "0");
  } catch {
    // A browser that will not remember the choice is no reason to refuse it.
  }
}

function fileFilters(filter: LibraryFilter, query: string): LibraryFileFilters {
  return {
    ...(filter.classification ? { classification: filter.classification } : {}),
    ...(filter.problem ? { problem: filter.problem } : {}),
    ...(filter.state ? { state: filter.state } : {}),
    ...(query ? { q: query } : {}),
    sort: "path",
    direction: "asc",
    page: 1,
    page_size: PAGE_SIZE,
  };
}

function NoLibrary() {
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

export function LibraryPage(): React.ReactElement {
  const libraries = useProcessingLibrariesQuery();
  const [params, setParams] = useSearchParams();
  const [libraryId, setLibraryId] = useState<number | null>(null);
  const [search, setSearch] = useState(params.get("q") ?? "");
  const [filter, setFilter] = useState<LibraryFilter>(NO_FILTER);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [openPath, setOpenPath] = useState<string | null>(params.get("path"));
  const [compact, setCompact] = useState(readCompact);
  const [now] = useState(() => Date.now());
  const query = useDebouncedValue(search.trim(), SEARCH_DEBOUNCE_MS);

  // The chosen library survives a reload and a link, so "open this library" is a plain URL.
  const chosen = useMemo(() => {
    const list = libraries.data ?? [];
    const wanted = libraryId ?? Number(params.get("library") ?? 0);
    return list.find((l) => l.id === wanted) ?? list[0] ?? null;
  }, [libraries.data, libraryId, params]);
  const chosenId = chosen?.id ?? 0;

  const libraryKeys = useMemo(
    () => [
      processingKeys.libraryFiles(chosenId),
      processingKeys.libraryOverview(chosenId),
    ],
    [chosenId],
  );
  useActivityStreamInvalidations(libraryKeys, {
    throttleMs: REFRESH_THROTTLE_MS,
  });

  const filters = useMemo(() => fileFilters(filter, query), [filter, query]);
  const overview = useLibraryOverviewQuery(chosenId, Boolean(chosen));
  const files = useLibraryFilesQuery(chosenId, filters, Boolean(chosen));
  const clean = useCleanLibraryFiles(chosenId);
  const leaveAlone = useSetLibraryFileLeaveAlone(chosenId);
  const groups = useMemo(
    () => groupFiles(files.data?.files ?? []),
    [files.data],
  );

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

  if (libraries.isPending) {
    return <PageLoading label="Loading your libraries" />;
  }
  if (libraries.isError) {
    return (
      <div className="mm-page">
        <ApiEntryError error={libraries.error} />
      </div>
    );
  }
  if (!chosen) return <NoLibrary />;

  const totals = overview.data?.totals;
  const loaded = files.data?.files ?? [];
  const selectedSaving = loaded
    .filter((f) => selected.has(f.path))
    .reduce((sum, f) => sum + f.estimated_bytes_saved, 0);

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
          <LibraryScanStatus
            libraryId={chosen.id}
            scan={overview.data?.scan ?? files.data?.scan ?? null}
            schedule={overview.data?.schedule}
            now={now}
          />
        }
      />

      <LibraryToolbar
        search={search}
        onSearch={setSearch}
        overview={overview.data}
        filter={filter}
        onFilter={setFilter}
        compact={compact}
        onCompact={(next) => {
          setCompact(next);
          saveCompact(next);
        }}
      />

      <LibraryCleanActions
        selected={selected}
        selectedSaving={selectedSaving}
        onClearSelection={() => setSelected(new Set())}
        clean={clean}
      />

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
        <LibraryTable
          libraryName={chosen.name}
          groups={groups}
          compact={compact}
          openPath={openPath}
          selected={selected}
          shownCount={files.data.filtered?.files ?? 0}
          total={files.data.total}
          onToggle={toggle}
          onOpen={setOpenPath}
        />
      )}

      <LibraryFileDrawer
        libraryId={chosen.id}
        libraryName={chosen.name}
        file={loaded.find((f) => f.path === openPath) ?? null}
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
