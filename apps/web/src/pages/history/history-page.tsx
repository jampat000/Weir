import { useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";

import { PageHeader } from "../../components/shell/page-header";
import { useCanEdit } from "../../lib/auth/can-edit";
import {
  useProcessingFilesQuery,
  useRequeueProcessingFiles,
} from "../../lib/processing/files-queries";
import { useProcessingLibrariesQuery } from "../../lib/processing/libraries-queries";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import { useNow } from "../../lib/ui/use-now";
import { HistoryDetail } from "./history-detail";
import {
  DEFAULT_PERIOD,
  HistoryFilters,
  PERIODS,
  type SetParam,
} from "./history-filters";
import { HistoryList } from "./history-list";
import {
  HISTORY_GROUPS,
  historyGroupOf,
  inGroup,
  newestFirst,
  type HistoryGroup,
} from "./history-model";

/** A running pass moves every few seconds; the list and the open file follow it while one does. */
const REFRESH_MS = 5000;
/** "min ago" moves on its own between refreshes. */
const TICK_MS = 15_000;
const FILES_LIMIT = 1000;

function RetryFailed({ libraryId }: { libraryId: number | undefined }) {
  const requeueFailed = useRequeueProcessingFiles();
  const [notice, setNotice] = useState<string | null>(null);
  return (
    <div className="mm-history-bulk">
      <button
        type="button"
        className={mmActionButtonClass({ variant: "secondary" })}
        disabled={requeueFailed.isPending}
        onClick={() => {
          setNotice(null);
          requeueFailed.mutate(
            {
              file_status: "processing_failed",
              library_id: libraryId,
              limit: FILES_LIMIT,
            },
            {
              onSuccess: (result) => setNotice(result.detail),
              onError: () =>
                setNotice("Those files could not be queued again."),
            },
          );
        }}
      >
        Try every failed file again
      </button>
      {notice ? (
        <span className="mm-history-note" role="status">
          {notice}
        </span>
      ) : null}
    </div>
  );
}

/**
 * History: every file Weir has touched, what it was, what Weir did and what came out. A place of its
 * own because a file's story is neither a Processing lane nor a system log: System › Logs keeps
 * Weir's own events, and this keeps the files.
 */
export function HistoryPage() {
  const [params, setParams] = useSearchParams();
  const group = (HISTORY_GROUPS.find((g) => g.id === params.get("show"))?.id ??
    "all") as HistoryGroup;
  const period =
    PERIODS.find((p) => p.id === params.get("within")) ?? DEFAULT_PERIOD;
  const libraryId = Number(params.get("library")) || undefined;
  const selectedId = Number(params.get("file")) || null;
  const now = useNow(TICK_MS);

  const files = useProcessingFilesQuery({
    limit: FILES_LIMIT,
    within_days: period.days,
    library_id: libraryId,
    path_contains: params.get("q") ?? undefined,
  });
  const libraries = useProcessingLibrariesQuery();
  const editable = useCanEdit();

  const anyWorking = (files.data?.files ?? []).some(
    (f) => historyGroupOf(f) === "working",
  );
  useEffect(() => {
    if (!anyWorking) return;
    const timer = window.setInterval(() => void files.refetch(), REFRESH_MS);
    return () => window.clearInterval(timer);
  }, [anyWorking, files]);

  const all = useMemo(() => newestFirst(files.data?.files ?? []), [files.data]);
  const shown = all.filter((f) => inGroup(f, group));
  const counts = Object.fromEntries(
    HISTORY_GROUPS.map((g) => [
      g.id,
      all.filter((f) => inGroup(f, g.id)).length,
    ]),
  ) as Record<HistoryGroup, number>;
  const selected = all.find((f) => f.id === selectedId) ?? shown[0] ?? null;

  const setParam: SetParam = (name, value) => {
    const next = new URLSearchParams(params);
    if (value === null || value === "") next.delete(name);
    else next.set(name, value);
    setParams(next, { replace: true });
  };

  return (
    <div className="mm-page" data-testid="history-page">
      <PageHeader
        title="History"
        lead="Every file Weir has touched: what it was, what Weir did, and what came out."
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

      {group === "failed" && editable && counts.failed > 0 ? (
        <RetryFailed libraryId={libraryId} />
      ) : null}

      {files.isError ? (
        <p className="mm-history-empty" role="alert">
          Weir could not read its file history. Check that it is still running,
          then refresh.
        </p>
      ) : files.isLoading ? (
        <p className="mm-history-empty">Reading history…</p>
      ) : shown.length === 0 ? (
        <p className="mm-history-empty">
          {all.length === 0
            ? "Nothing yet. Every file Weir picks up will be listed here, from the moment it arrives."
            : "No file matches. Try All, or look further back."}
        </p>
      ) : (
        <div className="mm-history-body">
          <HistoryList
            files={shown}
            selectedId={selected?.id ?? null}
            now={now}
            onPick={(id) => setParam("file", String(id))}
          />
          {selected ? (
            <HistoryDetail file={selected} now={now} editable={editable} />
          ) : null}
        </div>
      )}
    </div>
  );
}
