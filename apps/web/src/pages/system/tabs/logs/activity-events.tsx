import { useState, type ReactNode } from "react";
import { Panel } from "../../../../components/panels/panel";
import { PageLoading } from "../../../../components/shared/page-loading";
import {
  anyFilterSet,
  type ActivityLogFilters,
  type ActivityLogQuery,
} from "../../../../lib/activity/activity-filters";
import { useActivityRecentQuery } from "../../../../lib/activity/queries";
import { activityKeys } from "../../../../lib/activity/query-keys";
import { useActivityStreamInvalidations } from "../../../../lib/activity/use-activity-stream-invalidation";
import {
  fetchActivityExport,
  fetchActivityRecent,
} from "../../../../lib/api/activity-api";
import {
  isHttpErrorFromApi,
  isLikelyNetworkFailure,
} from "../../../../lib/api/error-guards";
import { errorMessage } from "../../../../lib/api/error-message";
import type { ActivityEventItem } from "../../../../lib/api/types";
import { useCanEdit } from "../../../../lib/auth/can-edit";
import { useHistoryResetMutation } from "../../../../lib/settings/queries";
import { fetchOperationalHistoryPreview } from "../../../../lib/settings/settings-api";
import type { HistoryResetResult } from "../../../../lib/settings/types";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import {
  useAppDateFormatter,
  useAppDayFormatter,
} from "../../../../lib/ui/mm-format-date";
import { plural } from "../../../../lib/ui/mm-plural";
import { ActivityLogFeed } from "./activity-log-feed";
import { eventsStatusWords } from "./activity-log-status";
import { ClearHistoryDialog } from "./clear-history-dialog";
import { useCalmFeed } from "./use-calm-feed";

type ExportFormat = "csv" | "json";

/** Slow enough that a burst of events doesn't refetch the log on every one of them (#710). */
const LOGS_THROTTLE_MS = 3_000;

const LOGS_KEYS = [activityKeys.recent] as const;

function download(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = filename;
  anchor.click();
  URL.revokeObjectURL(url);
}

/**
 * Weir's own events (System › Logs): the list for the filters it is given, with its exports and clearing, newest
 * first and live. The page remounts it when the filters change, so it starts from the first page each time.
 */
export function ActivityEvents({
  applied,
  queryFilters,
  onClearFilters,
  range,
}: {
  applied: ActivityLogFilters;
  queryFilters: ActivityLogQuery;
  onClearFilters: () => void;
  /** The dates a reader picked for themselves, above the list, when they have chosen to pick. */
  range: ReactNode;
}) {
  const [olderItems, setOlderItems] = useState<ActivityEventItem[]>([]);
  const [loadingOlder, setLoadingOlder] = useState(false);
  const [olderError, setOlderError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [exporting, setExporting] = useState<ExportFormat | null>(null);
  const [clearPreview, setClearPreview] = useState<HistoryResetResult | null>(
    null,
  );
  const [clearError, setClearError] = useState<string | null>(null);

  const canClear = useCanEdit();
  const resetHistory = useHistoryResetMutation();
  const dataKey = JSON.stringify(queryFilters);

  useActivityStreamInvalidations(LOGS_KEYS, { throttleMs: LOGS_THROTTLE_MS });
  const recent = useActivityRecentQuery(queryFilters);
  const fmt = useAppDateFormatter();
  const formatDay = useAppDayFormatter();
  const {
    feedRef,
    shown: held,
    show,
  } = useCalmFeed(recent.data, dataKey, olderItems.length > 0);

  if (recent.isPending) {
    return <PageLoading label="Loading activity" />;
  }

  if (recent.isError) {
    const err = recent.error;
    return (
      <div>
        <header>
          <p className="mm-page__lead">
            {isLikelyNetworkFailure(err)
              ? "Could not reach the Weir API."
              : isHttpErrorFromApi(err)
                ? "The server refused this request. Sign in again if needed."
                : "Could not load activity."}
          </p>
        </header>
        <p className="mm-page__lead font-mono text-sm text-mm-text3">
          {err.message}
        </p>
      </div>
    );
  }

  const liveData = recent.data;
  const shown = held ?? liveData;
  const itemById = new Map<number, ActivityEventItem>();
  for (const event of [...(shown.items ?? []), ...olderItems])
    itemById.set(event.id, event);
  const items = Array.from(itemById.values()).sort((a, b) => b.id - a.id);
  const matchingTotal = Math.max(Number(shown.total) || 0, items.length);
  const visibleItems = items.slice(0, matchingTotal || items.length);
  const shownMaxId = visibleItems.reduce((max, ev) => Math.max(max, ev.id), 0);
  const pendingCount =
    shown === liveData
      ? 0
      : (liveData.items ?? []).filter((ev) => ev.id > shownMaxId).length;
  const hasMore =
    Boolean(shown.has_more) || visibleItems.length < matchingTotal;
  const retentionDays = shown.retention_days;
  const problems = [
    { key: "action", text: actionError },
    { key: "older", text: olderError },
  ].filter((problem) => problem.text);

  async function loadOlderActivity() {
    const oldest = visibleItems.at(-1);
    if (!oldest || loadingOlder) return;
    setLoadingOlder(true);
    setOlderError(null);
    try {
      const page = await fetchActivityRecent({
        ...queryFilters,
        before_id: oldest.id,
      });
      setOlderItems((previous) => {
        const merged = new Map(previous.map((item) => [item.id, item]));
        for (const item of page.items ?? []) merged.set(item.id, item);
        return Array.from(merged.values()).sort((a, b) => b.id - a.id);
      });
    } catch {
      setOlderError("Could not load older activity. Try again.");
    } finally {
      setLoadingOlder(false);
    }
  }

  async function exportHistory(format: ExportFormat) {
    setExporting(format);
    setActionError(null);
    try {
      const { limit: _limit, ...exportFilters } = queryFilters;
      void _limit;
      const { blob, filename } = await fetchActivityExport(
        format,
        exportFilters,
      );
      download(blob, filename);
    } catch (e) {
      setActionError(errorMessage(e, "Could not export activity."));
    } finally {
      setExporting(null);
    }
  }

  async function startClearAll() {
    setActionError(null);
    setClearError(null);
    setNotice(null);
    try {
      setClearPreview(await fetchOperationalHistoryPreview());
    } catch (e) {
      setActionError(
        errorMessage(e, "Could not check what clearing history would remove."),
      );
    }
  }

  async function confirmClearAll(confirm: string) {
    setClearError(null);
    try {
      const out = await resetHistory.mutateAsync(confirm);
      setClearPreview(null);
      setNotice(
        `History cleared. Removed ${plural(out.activity_events_deleted, "Activity event", "Activity events")} and ${plural(out.jobs_deleted, "finished job", "finished jobs")}. No media file was touched.`,
      );
      setOlderItems([]);
      const refreshed = await recent.refetch();
      if (refreshed.data) show(refreshed.data);
    } catch (e) {
      setClearError(errorMessage(e, "Could not clear history."));
    }
  }

  const statusWords = eventsStatusWords({
    loaded: visibleItems.length,
    total: matchingTotal,
    filtered: anyFilterSet(applied),
    retentionDays,
    oldestDay: shown.oldest_event_at ? formatDay(shown.oldest_event_at) : null,
  });

  return (
    <div className="mm-sys-stack">
      {problems.length > 0 ? (
        <ul className="mm-interrupt" data-testid="activity-problems">
          {problems.map((problem) => (
            <li key={problem.key} className="mm-interrupt__item">
              <span className="mm-interrupt__text" role="alert">
                {problem.text}
              </span>
            </li>
          ))}
        </ul>
      ) : null}
      {notice ? (
        <p className="mm-quiet-note" role="status">
          {notice}
        </p>
      ) : null}

      <Panel
        title="Events"
        headingId="activity-history-heading"
        padded
        count={
          <span className="mm-activity-status" data-testid="activity-summary">
            <span className="mm-activity-summary__live" aria-hidden="true" />
            {statusWords}
          </span>
        }
        aside={
          <>
            {anyFilterSet(applied) ? (
              <button
                type="button"
                className={`${mmActionButtonClass({ variant: "secondary" })} mm-sys-btn`}
                onClick={onClearFilters}
              >
                Clear filters
              </button>
            ) : null}
            {(["csv", "json"] as const).map((format) => (
              <button
                key={format}
                type="button"
                className={`${mmActionButtonClass({ variant: "secondary" })} mm-sys-btn`}
                disabled={exporting !== null}
                onClick={() => void exportHistory(format)}
              >
                {exporting === format
                  ? "Exporting…"
                  : `Export ${format.toUpperCase()}`}
              </button>
            ))}
            {canClear ? (
              <button
                type="button"
                className={`${mmActionButtonClass({ variant: "secondary" })} mm-sys-btn mm-sys-btn--danger`}
                onClick={() => void startClearAll()}
              >
                Clear all history
              </button>
            ) : null}
          </>
        }
      >
        {range}
        <section
          ref={feedRef}
          className="mm-activity-list"
          data-testid="activity-feed"
        >
          {pendingCount > 0 ? (
            <div className="sticky top-2 z-10 flex justify-center">
              <button
                type="button"
                className={mmActionButtonClass({ variant: "primary" })}
                onClick={() => show(liveData)}
              >
                {`${plural(pendingCount, "new entry", "new entries")} — show`}
              </button>
            </div>
          ) : null}
          <ActivityLogFeed items={visibleItems} fmt={fmt} />
        </section>

        {hasMore ? (
          <div className="mt-4">
            <button
              type="button"
              className="mm-quiet-link"
              disabled={loadingOlder}
              onClick={() => void loadOlderActivity()}
            >
              {loadingOlder ? "Loading older…" : "Load older activity →"}
            </button>
          </div>
        ) : null}
      </Panel>

      {clearPreview ? (
        <ClearHistoryDialog
          preview={clearPreview}
          busy={resetHistory.isPending}
          error={clearError}
          onCancel={() => setClearPreview(null)}
          onConfirm={(confirm) => void confirmClearAll(confirm)}
        />
      ) : null}
    </div>
  );
}
