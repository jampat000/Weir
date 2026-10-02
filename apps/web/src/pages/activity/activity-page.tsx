import { useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";

import { Panel } from "../../components/panels/panel";
import { LoadError } from "../../components/shared/load-error";
import { PanelLoading } from "../../components/shared/page-loading";
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
import { ActivityCleanDetail } from "./activity-clean-detail";
import { ActivityDetail } from "./activity-detail";
import {
  ACTIVITY_GROUPS,
  hasRejectedFiles,
  activityEntries,
  inGroup,
  retryableFailures,
  type ActivityEntry,
  type ActivityGroup,
} from "./activity-entries";
import {
  DEFAULT_PERIOD,
  ActivityFilters,
  PERIODS,
  type SetParam,
} from "./activity-filters";
import { ActivityKeptList } from "./activity-kept-list";
import { ActivityList } from "./activity-list";
import { ProcessRejectedAgain } from "./activity-rejected-again";
import { ActivityRetentionNote } from "./activity-retention-note";
import { useActivityAttention } from "./use-activity-attention";

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
  entries: ActivityEntry[],
  fileId: number | null,
  cleanId: number | null,
): ActivityEntry | null {
  const chosen = entries.find(
    (entry) =>
      (entry.kind === "download" && entry.file.id === fileId) ||
      (entry.kind === "library_clean" && entry.clean.id === cleanId),
  );
  return chosen ?? entries[0] ?? null;
}

/** What an empty list says: that nothing needs a person, that nothing has been handled yet, or that nothing matches. */
function emptyWords(group: ActivityGroup, handled: number): string {
  if (group === "attention") return "No file is waiting on you.";
  return handled === 0
    ? "Nothing yet. Every file Weir picks up will be listed here, from the moment it arrives."
    : "No file matches. Try All, or look further back.";
}

/**
 * Activity: every file Weir has handled, new downloads and library cleans alike, what it was, what
 * Weir did, and what came out. A place of its own because a file's story is neither a Processing lane
 * nor a system log: System › Logs keeps Weir's own events, and this keeps the files (#695).
 */
export function ActivityPage() {
  const [params, setParams] = useSearchParams();
  const group = (ACTIVITY_GROUPS.find((g) => g.id === params.get("show"))?.id ??
    "all") as ActivityGroup;
  const period =
    PERIODS.find((p) => p.id === params.get("within")) ?? DEFAULT_PERIOD;
  const libraryId = Number(params.get("library")) || undefined;
  const selectedFileId = Number(params.get("file")) || null;
  const selectedCleanId = Number(params.get("clean")) || null;
  const now = useNow(TICK_MS);
  const [removedNotice, setRemovedNotice] = useState<string | null>(null);
  // Where the header's pickers go when it has no room for them: the aside of whichever card is on the page.
  const [pickersSlot, setPickersSlot] = useState<HTMLDivElement | null>(null);
  const pickersAside = <div ref={setPickersSlot} className="contents" />;

  const query = {
    within_days: period.days,
    library_id: libraryId,
    path_contains: params.get("q") ?? undefined,
  };
  const files = useFileHistoryQuery({ ...query, limit: FILES_LIMIT });
  const attention = useActivityAttention(
    libraryId ?? null,
    params.get("q") ?? "",
  );
  const cleans = useLibraryCleansQuery(query);
  const libraries = useProcessingLibrariesQuery();
  const kept = useKeptFilesQuery();
  const editable = useCanEdit();

  const all = useMemo(
    () => activityEntries(files.data?.files ?? [], cleans.data?.cleans ?? []),
    [files.data, cleans.data],
  );
  const needsYou = group === "attention";
  const shown = needsYou
    ? attention.entries
    : all.filter((entry) => inGroup(entry, group));
  const counts = {
    ...(Object.fromEntries(
      ACTIVITY_GROUPS.map((g) => [
        g.id,
        all.filter((entry) => inGroup(entry, g.id)).length,
      ]),
    ) as Record<ActivityGroup, number>),
    // The Needs you count is the sidebar badge's, from the same source, whichever period is chosen.
    attention: attention.count,
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

  const pickEntry = (entry: ActivityEntry) => {
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
    <div className="mm-page" data-testid="activity-page">
      <ActivityFilters
        query={params.get("q") ?? ""}
        group={group}
        counts={counts}
        libraryId={libraryId}
        periodId={period.id}
        libraries={libraries.data ?? []}
        setParam={setParam}
        cardSlot={pickersSlot}
      />

      {group === "failed" && editable ? (
        <div className="mm-history-bulks">
          <RetryFailed failed={retryableFailures(shown)} />
          {hasRejectedFiles(shown) ? (
            <ProcessRejectedAgain
              libraryId={libraryId}
              libraryName={
                libraries.data?.find((l) => l.id === libraryId)?.name
              }
            />
          ) : null}
        </div>
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
          <Panel
            title="Kept files"
            className="mm-history-list"
            count={`${(kept.data?.files.length ?? 0).toLocaleString()} kept`}
            aside={pickersAside}
          >
            <ActivityKeptList
              files={kept.data?.files ?? []}
              editable={editable}
              onProcessed={setRemovedNotice}
            />
          </Panel>
        )
      ) : !needsYou && files.isError ? (
        <LoadError thing="your file activity" error={files.error} />
      ) : !needsYou && files.isLoading ? (
        <PanelLoading label="Reading activity…" />
      ) : shown.length === 0 ? (
        <p className="mm-history-empty">{emptyWords(group, all.length)}</p>
      ) : (
        <div className="mm-history-body">
          <Panel
            title="Files"
            count={`${shown.length.toLocaleString()} shown`}
            className="mm-history-list"
            aside={pickersAside}
          >
            <ActivityList
              entries={shown}
              selectedKey={selected?.key ?? null}
              now={now}
              onPick={pickEntry}
            />
          </Panel>
          {selected?.kind === "download" ? (
            <ActivityDetail
              file={selected.file}
              now={now}
              editable={editable}
              onRemoved={setRemovedNotice}
            />
          ) : selected?.kind === "library_clean" ? (
            <ActivityCleanDetail clean={selected.clean} />
          ) : null}
        </div>
      )}

      <ActivityRetentionNote />
    </div>
  );
}
