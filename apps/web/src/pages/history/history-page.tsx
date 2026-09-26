import { useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";

import { LoadError } from "../../components/shared/load-error";
import { PanelLoading } from "../../components/shared/page-loading";
import { PageHeader } from "../../components/shell/page-header";
import { useCanEdit } from "../../lib/auth/can-edit";
import {
  useFileHistoryQuery,
  useLibraryCleansQuery,
  useRequeueProcessingFiles,
} from "../../lib/processing/files-queries";
import { useKeptFilesQuery } from "../../lib/processing/kept-files-queries";
import { useProcessingLibrariesQuery } from "../../lib/processing/libraries-queries";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import { useNow } from "../../lib/ui/use-now";
import { HistoryCleanDetail } from "./history-clean-detail";
import { HistoryDetail } from "./history-detail";
import {
  HISTORY_GROUPS,
  historyEntries,
  inGroup,
  retryableFailures,
  type HistoryEntry,
  type HistoryGroup,
} from "./history-entries";
import {
  DEFAULT_PERIOD,
  HistoryFilters,
  PERIODS,
  type SetParam,
} from "./history-filters";
import { HistoryKeptList } from "./history-kept-list";
import { HistoryList } from "./history-list";

/** "min ago" moves on its own between refreshes. */
const TICK_MS = 15_000;
const FILES_LIMIT = 1000;

function RetryFailed({ failed }: { failed: { id: number }[] }) {
  const requeueFailed = useRequeueProcessingFiles();
  const [notice, setNotice] = useState<string | null>(null);
  if (failed.length === 0) return null;
  return (
    <div className="mm-history-bulk">
      <button
        type="button"
        className={mmActionButtonClass({ variant: "secondary" })}
        disabled={requeueFailed.isPending}
        onClick={() => {
          setNotice(null);
          requeueFailed.mutate(
            { file_ids: failed.map((f) => f.id) },
            {
              onSuccess: (result) => setNotice(result.detail),
              onError: () =>
                setNotice("Those files could not be queued again."),
            },
          );
        }}
      >
        Try the {failed.length} failed {failed.length === 1 ? "file" : "files"}{" "}
        again
      </button>
      {notice ? (
        <span className="mm-history-note" role="status">
          {notice}
        </span>
      ) : null}
    </div>
  );
}

/** The chosen entry, from either `?file=` (a download) or `?clean=` (a library clean). */
function selectedEntry(
  entries: HistoryEntry[],
  fileId: number | null,
  cleanId: number | null,
): HistoryEntry | null {
  const chosen = entries.find(
    (entry) =>
      (entry.kind === "download" && entry.file.id === fileId) ||
      (entry.kind === "library_clean" && entry.clean.id === cleanId),
  );
  return chosen ?? entries[0] ?? null;
}

/**
 * History: every file Weir has handled, new downloads and library cleans alike, what it was, what
 * Weir did, and what came out. A place of its own because a file's story is neither a Processing lane
 * nor a system log: System › Logs keeps Weir's own events, and this keeps the files (#695).
 */
export function HistoryPage() {
  const [params, setParams] = useSearchParams();
  const group = (HISTORY_GROUPS.find((g) => g.id === params.get("show"))?.id ??
    "all") as HistoryGroup;
  const period =
    PERIODS.find((p) => p.id === params.get("within")) ?? DEFAULT_PERIOD;
  const libraryId = Number(params.get("library")) || undefined;
  const selectedFileId = Number(params.get("file")) || null;
  const selectedCleanId = Number(params.get("clean")) || null;
  const now = useNow(TICK_MS);
  const [removedNotice, setRemovedNotice] = useState<string | null>(null);

  const query = {
    within_days: period.days,
    library_id: libraryId,
    path_contains: params.get("q") ?? undefined,
  };
  const files = useFileHistoryQuery({ ...query, limit: FILES_LIMIT });
  const cleans = useLibraryCleansQuery(query);
  const libraries = useProcessingLibrariesQuery();
  const kept = useKeptFilesQuery();
  const editable = useCanEdit();

  const all = useMemo(
    () => historyEntries(files.data?.files ?? [], cleans.data?.cleans ?? []),
    [files.data, cleans.data],
  );
  const shown = all.filter((entry) => inGroup(entry, group));
  const counts = {
    ...(Object.fromEntries(
      HISTORY_GROUPS.map((g) => [
        g.id,
        all.filter((entry) => inGroup(entry, g.id)).length,
      ]),
    ) as Record<HistoryGroup, number>),
    // A kept file has no `files` row left to count as an entry (#786 review of #785), so its count comes from
    // the kept-files list instead of the entry-based counting every other chip uses.
    kept: kept.data?.files.length ?? 0,
  };
  const selected = selectedEntry(shown, selectedFileId, selectedCleanId);
  const cappedAtLimit =
    group !== "kept" && (files.data?.returned ?? 0) >= FILES_LIMIT;

  const setParam: SetParam = (name, value) => {
    setRemovedNotice(null);
    const next = new URLSearchParams(params);
    if (value === null || value === "") next.delete(name);
    else next.set(name, value);
    setParams(next, { replace: true });
  };

  const pickEntry = (entry: HistoryEntry) => {
    setRemovedNotice(null);
    const next = new URLSearchParams(params);
    if (entry.kind === "download") {
      next.set("file", String(entry.file.id));
      next.delete("clean");
    } else {
      next.set("clean", String(entry.clean.id));
      next.delete("file");
    }
    setParams(next, { replace: true });
  };

  return (
    <div className="mm-page" data-testid="history-page">
      <PageHeader
        title="History"
        lead="Every file Weir has handled, new downloads and library cleans: what it was, what Weir did, and what came out."
      />
      <HistoryFilters
        query={params.get("q") ?? ""}
        group={group}
        counts={counts}
        libraryId={libraryId}
        periodId={period.id}
        libraries={libraries.data ?? []}
        setParam={setParam}
      />

      {group === "failed" && editable ? (
        <RetryFailed failed={retryableFailures(shown)} />
      ) : null}

      {cappedAtLimit ? (
        <p className="mm-history-note" role="status">
          Showing the newest {FILES_LIMIT.toLocaleString()} downloads. Narrow
          the search or the period to see more.
        </p>
      ) : null}

      {removedNotice ? (
        <p className="mm-history-note" role="status">
          {removedNotice}
        </p>
      ) : null}

      {group === "kept" ? (
        kept.isError ? (
          <LoadError thing="your kept files" error={kept.error} />
        ) : kept.isLoading ? (
          <PanelLoading label="Reading kept files…" />
        ) : (
          <HistoryKeptList
            files={kept.data?.files ?? []}
            editable={editable}
            onProcessed={setRemovedNotice}
          />
        )
      ) : files.isError ? (
        <LoadError thing="your file history" error={files.error} />
      ) : files.isLoading ? (
        <PanelLoading label="Reading history…" />
      ) : shown.length === 0 ? (
        <p className="mm-history-empty">
          {all.length === 0
            ? "Nothing yet. Every file Weir picks up will be listed here, from the moment it arrives."
            : "No file matches. Try All, or look further back."}
        </p>
      ) : (
        <div className="mm-history-body">
          <HistoryList
            entries={shown}
            selectedKey={selected?.key ?? null}
            now={now}
            onPick={pickEntry}
          />
          {selected?.kind === "download" ? (
            <HistoryDetail
              file={selected.file}
              now={now}
              editable={editable}
              onRemoved={setRemovedNotice}
            />
          ) : selected?.kind === "library_clean" ? (
            <HistoryCleanDetail clean={selected.clean} />
          ) : null}
        </div>
      )}
    </div>
  );
}
