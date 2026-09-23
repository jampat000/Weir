import {
  compactActivityTitle,
  eventDisplay,
} from "../../../../lib/activity/activity-display";
import { groupActivityFeed } from "../../../../lib/activity/activity-groups";
import { summarizeRun } from "../../../../lib/activity/activity-runs";
import type { ActivityEventItem } from "../../../../lib/api/types";
import { plural } from "../../../../lib/ui/mm-plural";
import { ActivityEventRow } from "./activity-event-row";

function firstAndLatest(
  events: ActivityEventItem[],
  fmt: (iso: string) => string,
): string {
  return `first ${fmt(events.at(-1)?.created_at ?? "")} · latest ${fmt(events[0].created_at)}`;
}

/** The entries, newest first: a run folds under one summary, repeated failures under another. */
export function ActivityLogFeed({
  items,
  fmt,
}: {
  items: ActivityEventItem[];
  fmt: (iso: string) => string;
}) {
  if (items.length === 0) {
    return (
      <div className="mm-activity-list__empty">
        No activity matched the current filters.
      </div>
    );
  }
  return (
    <>
      {groupActivityFeed(items).map((group) => {
        if (group.kind === "run") {
          const summary = summarizeRun(group.events);
          return (
            <details
              key={group.key}
              className="mm-activity-cluster mm-activity-cluster--run"
              data-testid="activity-run"
            >
              <summary className="mm-activity-cluster__summary">
                <span
                  className={`mm-activity-event-icon${summary.failed > 0 ? " mm-activity-event-icon--error" : ""}`}
                  aria-hidden="true"
                >
                  {summary.failed > 0 ? "!" : "✓"}
                </span>
                <span className="mm-activity-cluster__text">
                  <strong>{summary.headline}</strong>
                  <small>
                    {plural(group.events.length, "entry", "entries")} ·{" "}
                    {firstAndLatest(group.events, fmt)}
                  </small>
                </span>
                {summary.failed > 0 ? (
                  <span className="mm-status-badge mm-status-badge--failed">
                    {summary.failed} failed
                  </span>
                ) : null}
              </summary>
              <div className="mm-activity-cluster__events">
                {group.events.map((ev) => (
                  <ActivityEventRow key={ev.id} ev={ev} compact fmt={fmt} />
                ))}
              </div>
            </details>
          );
        }
        if (group.kind === "repeat") {
          return (
            <ActivityEventRow
              key={group.key}
              ev={group.events[0]}
              repeats={group.events.length > 1 ? group.events : undefined}
              fmt={fmt}
            />
          );
        }
        if (group.events.length === 1) {
          return (
            <ActivityEventRow
              key={group.events[0].id}
              ev={group.events[0]}
              fmt={fmt}
            />
          );
        }
        return (
          <details
            key={group.key}
            className="mm-activity-cluster"
            data-testid="activity-cluster"
          >
            <summary className="mm-activity-cluster__summary">
              <span
                className="mm-activity-event-icon mm-activity-event-icon--error"
                aria-hidden="true"
              >
                !
              </span>
              <span className="mm-activity-cluster__text">
                <strong>{group.events.length} repeated failures</strong>
                <small>
                  {compactActivityTitle(eventDisplay(group.events[0]).title)} ·{" "}
                  {firstAndLatest(group.events, fmt)}
                </small>
              </span>
              <span className="mm-status-badge mm-status-badge--failed">
                Review
              </span>
            </summary>
            <div className="mm-activity-cluster__events">
              {group.events.map((ev) => (
                <ActivityEventRow key={ev.id} ev={ev} compact fmt={fmt} />
              ))}
            </div>
          </details>
        );
      })}
    </>
  );
}
