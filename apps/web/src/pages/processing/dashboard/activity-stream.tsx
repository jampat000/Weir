import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";

import { Panel } from "../../../components/panels/panel";
import { useActivityRecentQuery } from "../../../lib/activity/queries";
import { classNames } from "../../../lib/ui/class-names";
import { motionAllowed } from "../../../lib/ui/motion-allowed";
import { StreamIcon } from "./stream-icons";
import {
  buildStream,
  streamWhen,
  type StreamPart,
  type StreamRow,
} from "./stream-model";

/** Enough events to fill the list after the progress frames and routine entries are set aside. */
const RECENT_EVENTS = 80;
const LOG_PATH = "/system?tab=logs";
const NOTHING_YET =
  "Nothing has happened yet. What Weir does shows up here as it works.";

function Sentence({ parts }: { parts: readonly StreamPart[] }) {
  return parts.map((part, index) =>
    typeof part === "string" ? (
      <span key={index}>{part}</span>
    ) : (
      <b key={index}>{part.bold}</b>
    ),
  );
}

function Line({
  row,
  now,
  fresh,
}: {
  row: StreamRow;
  now: number;
  fresh: boolean;
}) {
  return (
    <li
      className={classNames("mm-stream__item", fresh && "mm-stream__item--new")}
    >
      <Link to={row.to} className="mm-stream__link">
        <span
          aria-hidden="true"
          className={`mm-stream__icon mm-stream__icon--${row.tone}`}
        >
          <StreamIcon tone={row.tone} />
        </span>
        <span className="mm-stream__text">
          <span className="mm-stream__what">
            <Sentence parts={row.parts} />
          </span>
          <span className="mm-stream__note">
            {row.times > 1 ? `${row.times.toLocaleString()} times · ` : ""}
            {row.note}
            {row.note ? " · " : ""}
            <time dateTime={row.at}>{streamWhen(row.at, now)}</time>
          </span>
        </span>
      </Link>
    </li>
  );
}

type ActivityStreamProps = {
  now: number;
  /** Only what happened to this workflow's files; everything when null or left out. */
  workflowId?: number | null;
};

/**
 * What Weir just did, newest first, from the same feed the Activity log reads. A line that arrives while
 * the page is open slides in and glows once; lines already there when the page opened do not.
 */
export function ActivityStream({
  now,
  workflowId = null,
}: ActivityStreamProps) {
  const recent = useActivityRecentQuery({
    limit: RECENT_EVENTS,
    library_id: workflowId ?? undefined,
  });
  const items = recent.data?.items;
  const stream = useMemo(() => buildStream(items ?? []), [items]);
  const newestId = items?.[0]?.id ?? null;
  const [openedAt, setOpenedAt] = useState<number | null>(null);
  useEffect(() => {
    if (openedAt === null && newestId !== null) setOpenedAt(newestId);
  }, [openedAt, newestId]);
  const animate = motionAllowed();
  return (
    <Panel
      title="Activity"
      count={
        stream.routine > 0
          ? `${stream.routine.toLocaleString()} routine left out`
          : "as it happens"
      }
      to={LOG_PATH}
      toLabel="All activity"
    >
      {stream.rows.length === 0 ? (
        <p className="mm-stream__empty">{NOTHING_YET}</p>
      ) : (
        <ul className="mm-stream__list" data-testid="live-stream">
          {stream.rows.map((row) => (
            <Line
              key={row.key}
              row={row}
              now={now}
              fresh={animate && openedAt !== null && row.id > openedAt}
            />
          ))}
        </ul>
      )}
    </Panel>
  );
}
