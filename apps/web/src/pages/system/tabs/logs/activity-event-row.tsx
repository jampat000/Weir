import type { ReactNode } from "react";

import { FileProgressDetail } from "../../../../components/activity/file-progress-detail";
import { RemuxPassDetail } from "../../../../components/activity/remux-pass-detail";
import {
  compactActivityTitle,
  eventDisplay,
  inlineDetailOf,
  type ActivityDisplay,
  type ActivityTone,
} from "../../../../lib/activity/activity-display";
import { activityTriggerLabel } from "../../../../lib/activity/activity-runs";
import { parseActivityDetail } from "../../../../lib/activity/detail";
import {
  FILE_PROGRESS_EVENT,
  REMUX_PASS_COMPLETED_EVENT,
} from "../../../../lib/activity/event-types";
import type { ActivityEventItem } from "../../../../lib/api/types";

function toneIcon(tone: ActivityTone): string {
  return tone === "success" ? "✓" : tone === "info" ? "·" : "!";
}

/** What "Details" opens to: a structured card for a processing pass, the text itself otherwise. */
function EventDetails({
  ev,
  display,
}: {
  ev: ActivityEventItem;
  display: ActivityDisplay;
}) {
  if (!display.detail) return null;
  let body: ReactNode = null;
  if (ev.event_type === FILE_PROGRESS_EVENT) {
    body = <FileProgressDetail detail={display.detail} />;
  } else if (ev.event_type === REMUX_PASS_COMPLETED_EVENT) {
    body = <RemuxPassDetail detail={display.detail} />;
  } else if (
    ev.event_type === "system.reconciliation.repair" &&
    parseActivityDetail(ev.detail)
  ) {
    body = (
      <div className="mm-activity-item__structured">
        <p className="mm-activity-item__structured-text">{ev.detail}</p>
      </div>
    );
  }
  if (!body && inlineDetailOf(ev, display) !== null) {
    return null;
  }
  return (
    <details className="mm-activity-item__more">
      <summary>Details</summary>
      <div className="mm-activity-item__more-body">
        {body ?? (
          <p className="mm-activity-item__more-text">{display.detail}</p>
        )}
      </div>
    </details>
  );
}

/** One entry in the event log, or one row standing for several identical entries. */
export function ActivityEventRow({
  ev,
  fmt,
  compact = false,
  repeats,
}: {
  ev: ActivityEventItem;
  fmt: (iso: string) => string;
  compact?: boolean;
  /** Identical earlier entries folded into this row (newest first, this one included). */
  repeats?: ActivityEventItem[];
}) {
  const display = eventDisplay(ev);
  const triggerLabel = activityTriggerLabel(ev.trigger);
  const count = repeats?.length ?? 1;
  const earliest = repeats?.at(-1)?.created_at;
  const inlineDetail = inlineDetailOf(ev, display);
  return (
    <article
      className={`mm-activity-item mm-activity-item--${display.tone}${compact ? " mm-activity-item--compact" : ""}`}
      data-testid="activity-row"
    >
      <span
        className={`mm-activity-event-icon mm-activity-event-icon--${display.tone}`}
        aria-hidden="true"
      >
        {toneIcon(display.tone)}
      </span>
      <div className="mm-activity-item__main">
        <div className="mm-activity-item__head">
          <h2 className="mm-activity-item__title" title={display.title}>
            {compactActivityTitle(display.title)}
          </h2>
          {count > 1 ? (
            <span
              className="mm-activity-item__count"
              data-testid="activity-repeat-count"
              title={`${count} identical entries`}
            >
              ×{count}
            </span>
          ) : null}
          {display.chip ? (
            <span
              className={`mm-activity-item__badge mm-activity-chip--${display.tone}`}
            >
              {display.chip}
            </span>
          ) : null}
          {triggerLabel ? (
            <span
              className="mm-activity-item__badge mm-activity-item__badge--quiet"
              data-testid="activity-trigger-chip"
              title="Why this happened"
            >
              {triggerLabel}
            </span>
          ) : null}
        </div>
        {!compact ? (
          <p className="mm-activity-item__summary">
            <span>{display.summary}</span>
            {inlineDetail ? (
              <>
                <span aria-hidden="true"> · </span>
                <span className="mm-activity-item__detail">{inlineDetail}</span>
              </>
            ) : null}
          </p>
        ) : null}
        {!compact ? <EventDetails ev={ev} display={display} /> : null}
      </div>
      <time
        className="mm-activity-item__time"
        dateTime={ev.created_at}
        title={
          count > 1 && earliest
            ? `Latest ${fmt(ev.created_at)}, first ${fmt(earliest)}`
            : undefined
        }
      >
        {fmt(ev.created_at)}
      </time>
    </article>
  );
}
