import { useMemo, useState } from "react";
import { Link } from "react-router-dom";

import { PageLoading } from "../../../../components/shared/page-loading";
import { eventOptions } from "../../../../lib/activity/activity-display";
import {
  EMPTY_ACTIVITY_FILTERS,
  activityLogQuery,
  anyFilterSet,
  type ActivityLogFilters,
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
import { useAppDateFormatter } from "../../../../lib/ui/mm-format-date";
import { plural } from "../../../../lib/ui/mm-plural";
import { ActivityLogFeed } from "./activity-log-feed";
import { ActivityLogFiltersSection } from "./activity-log-filters";
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

/** Weir's own events (System › Logs): filters, exports and clearing, newest first and live. */
export function ActivityLog() {
  const [draft, setDraft] = useState<ActivityLogFilters>(
    EMPTY_ACTIVITY_FILTERS,
  );
  const [applied, setApplied] = useState<ActivityLogFilters>(
    EMPTY_ACTIVITY_FILTERS,
  );
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
  const queryFilters = useMemo(() => activityLogQuery(applied), [applied]);
  const dataKey = JSON.stringify(queryFilters);

  useActivityStreamInvalidations(LOGS_KEYS, { throttleMs: LOGS_THROTTLE_MS });
  const recent = useActivityRecentQuery(queryFilters);
  const fmt = useAppDateFormatter();
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
  // `total` counts everything the filters match, so this is honest however far the reader has paged.
  const notLoaded = Math.max(0, matchingTotal - visibleItems.length);
  const problems = [
    { key: "action", text: actionError },
    { key: "older", text: olderError },
  ].filter((problem) => problem.text);

  function applyFilters(next: ActivityLogFilters) {
    setDraft(next);
    setApplied(next);
    setOlderItems([]);
    setOlderError(null);
  }

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

  return (
    <div>
      <header>
        {typeof retentionDays === "number" ? (
          <p
            className="mt-1 text-sm text-mm-text2"
            data-testid="activity-retention"
          >
            {retentionDays > 0
              ? `History goes back ${plural(retentionDays, "day", "days")}${shown.oldest_event_at ? ` (oldest entry ${fmt(shown.oldest_event_at)})` : ""}.`
              : "History is kept until you clear it."}{" "}
            <Link
              to="/system?tab=history#activity-retention"
              className="text-mm-gold underline-offset-2 hover:underline"
            >
              Change how long history is kept
            </Link>
          </p>
        ) : null}
      </header>

      <div className="mm-quiet-stack">
        <div className="mm-lead">
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

          <p
            className="mm-quiet-note inline-flex flex-wrap items-center gap-2"
            data-testid="activity-summary"
          >
            <span className="mm-activity-summary__live" aria-hidden="true" />
            <span>
              Showing {visibleItems.length} of {matchingTotal}{" "}
              {matchingTotal === 1 ? "event" : "events"}
              {anyFilterSet(applied) ? " matching your filters" : ""} · live
            </span>
          </p>

          <p className="mm-lead-caption">
            <span>Filters apply to this list and to both exports.</span>
            <span>
              {notLoaded > 0
                ? `${plural(notLoaded, "older entry is", "older entries are")} not loaded yet.`
                : "Everything that matches is loaded."}
            </span>
          </p>

          {notice ? (
            <p className="mm-quiet-note" role="status">
              {notice}
            </p>
          ) : null}
        </div>

        <ActivityLogFiltersSection
          draft={draft}
          setDraft={setDraft}
          applied={applied}
          eventOptions={eventOptions(visibleItems)}
          onApply={applyFilters}
        />

        <section
          className="mm-quiet-section"
          aria-labelledby="activity-history-heading"
        >
          <div className="mm-quiet-section__head">
            <h2
              id="activity-history-heading"
              className="mm-quiet-section__title"
            >
              Events
            </h2>
            <div className="mm-quiet-section__aside">
              {(["csv", "json"] as const).map((format) => (
                <button
                  key={format}
                  type="button"
                  className="mm-quiet-link"
                  disabled={exporting !== null}
                  onClick={() => void exportHistory(format)}
                >
                  {exporting === format
                    ? "Exporting…"
                    : `Export ${format.toUpperCase()} →`}
                </button>
              ))}
              {canClear ? (
                <button
                  type="button"
                  className="mm-quiet-link"
                  onClick={() => void startClearAll()}
                >
                  Clear all history →
                </button>
              ) : null}
            </div>
          </div>
          <div className="mm-quiet-section__body">
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
          </div>
        </section>
      </div>

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
