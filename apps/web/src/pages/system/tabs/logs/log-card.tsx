import { useState, type ReactNode } from "react";

import { Panel } from "../../../../components/panels/panel";
import { LoadError } from "../../../../components/shared/load-error";
import {
  eventLabel,
  eventOptions,
} from "../../../../lib/activity/activity-display";
import { useCanEdit } from "../../../../lib/auth/can-edit";
import type { SystemLogQuery } from "../../../../lib/system/system-log-api";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { plural } from "../../../../lib/ui/mm-plural";
import { LogClearEvents } from "./log-clear-events";
import { LogExportMenu } from "./log-export-menu";
import { anyLogFilterSet, refineCount, type LogFilters } from "./log-filters";
import { LogList } from "./log-list";
import { LogRange } from "./log-range";
import { LogRefine } from "./log-refine";
import { logSummaryWords } from "./log-summary";
import type { LogEntries } from "./use-log-entries";

/** The events in the list, as the options of the event type picker: those present, and the one chosen when it is not. */
function eventTypeOptions(
  entries: LogEntries,
  chosen: string,
): { value: string; label: string }[] {
  const options = eventOptions(
    entries.rows.flatMap((row) => (row.event ? [row.event] : [])),
  );
  return chosen && !options.some((option) => option.value === chosen)
    ? [...options, { value: chosen, label: eventLabel(chosen) }]
    : options;
}

/**
 * The Log card: one list of everything that happened, newest first, under the day each thing fell on. Its header holds a
 * line on what the list holds, a way to refine it past what the header's filters can, Export, and Clear events.
 */
export function LogCard({
  toolbar,
  filters,
  query,
  entries,
  watchFeed,
  expandedId,
  onToggle,
  onChange,
  onClearFilters,
  onRelated,
}: {
  /** The filters the header had no room for, to show above the list; null when it held them all. */
  toolbar: ReactNode;
  filters: LogFilters;
  query: SystemLogQuery;
  entries: LogEntries;
  /** Hands the list's element over, so new rows can wait while the reader is further down. */
  watchFeed: (element: HTMLElement | null) => void;
  expandedId: string | null;
  onToggle: (id: string) => void;
  onChange: (next: Partial<LogFilters>) => void;
  onClearFilters: () => void;
  onRelated: (jobId: number) => void;
}) {
  const canClear = useCanEdit();
  const [refineOpen, setRefineOpen] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const refining = refineCount(filters);
  const filtered = anyLogFilterSet(filters);
  const problems = [problem, entries.olderError].filter(Boolean);

  return (
    <>
      {problems.length > 0 ? (
        <ul className="mm-interrupt" data-testid="logs-problems">
          {problems.map((text) => (
            <li key={text} className="mm-interrupt__item">
              <span className="mm-interrupt__text" role="alert">
                {text}
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
        title="Log"
        headingId="logs-heading"
        padded
        dataTestId="logs-card"
        count={
          <span className="mm-log-status" data-testid="log-summary">
            <span className="mm-log-live" aria-hidden="true" />
            {logSummaryWords({
              loaded: entries.rows.length,
              total: entries.total,
              filtered,
            })}
          </span>
        }
        aside={
          <>
            {filtered ? (
              <button
                type="button"
                className={`${mmActionButtonClass({ variant: "secondary" })} mm-sys-btn`}
                onClick={onClearFilters}
              >
                Clear filters
              </button>
            ) : null}
            <button
              type="button"
              className={`${mmActionButtonClass({ variant: "secondary" })} mm-sys-btn`}
              aria-expanded={refineOpen || refining > 0}
              onClick={() => setRefineOpen((open) => !open)}
            >
              {refining > 0 ? `Refine · ${refining}` : "Refine"}
            </button>
            <LogExportMenu query={query} onProblem={setProblem} />
            {canClear ? (
              <LogClearEvents
                onProblem={setProblem}
                onCleared={(done) => {
                  setNotice(done);
                  void entries.reload();
                }}
              />
            ) : null}
          </>
        }
      >
        {toolbar}
        {refineOpen || refining > 0 ? (
          <LogRefine
            filters={filters}
            eventTypes={eventTypeOptions(entries, filters.eventType)}
            onChange={onChange}
          />
        ) : null}
        {filters.when === "custom" ? (
          <LogRange from={filters.from} to={filters.to} onChange={onChange} />
        ) : null}
        <section ref={watchFeed} aria-busy={entries.loading}>
          {entries.arrived > 0 ? (
            <div className="sticky top-2 z-10 flex justify-center">
              <button
                type="button"
                className={mmActionButtonClass({ variant: "primary" })}
                onClick={entries.showArrived}
              >
                {`${plural(entries.arrived, "new entry", "new entries")} — show`}
              </button>
            </div>
          ) : null}
          <LogBody
            entries={entries}
            filtered={filtered}
            expandedId={expandedId}
            onToggle={onToggle}
            onClearFilters={onClearFilters}
            actions={{ canAct: canClear, onRelated }}
          />
        </section>
        {entries.hasMore ? (
          <div className="mt-4">
            <button
              type="button"
              className="mm-quiet-link"
              disabled={entries.loadingMore}
              onClick={entries.loadMore}
            >
              {entries.loadingMore ? "Loading older…" : "Load older entries →"}
            </button>
          </div>
        ) : null}
      </Panel>
    </>
  );
}

/** The list, or what stands in its place: that it is loading, that it could not be read, or that nothing matches. */
function LogBody({
  entries,
  filtered,
  expandedId,
  onToggle,
  onClearFilters,
  actions,
}: {
  entries: LogEntries;
  filtered: boolean;
  expandedId: string | null;
  onToggle: (id: string) => void;
  onClearFilters: () => void;
  actions: { canAct: boolean; onRelated: (jobId: number) => void };
}) {
  if (entries.error) return <LoadError thing="the log" error={entries.error} />;
  if (entries.rows.length === 0 && entries.loading) {
    return <p className="mm-quiet-note">Loading the log…</p>;
  }
  if (entries.rows.length === 0) {
    return (
      <div className="space-y-2 py-4" data-testid="log-empty">
        <p className="mm-quiet-note">
          {filtered
            ? "Nothing in the log matches these filters."
            : "Nothing has been logged yet."}
        </p>
        {filtered ? (
          <button
            type="button"
            className="mm-quiet-link"
            onClick={onClearFilters}
          >
            Clear filters →
          </button>
        ) : null}
      </div>
    );
  }
  return (
    <LogList
      rows={entries.rows}
      expandedId={expandedId}
      onToggle={onToggle}
      actions={actions}
    />
  );
}
