import { Link } from "react-router-dom";

import type { FinishedFile } from "../../lib/activity/processing-outcome";
import { FinishedRow } from "./lane-cards";
import { EmptyLane, Lane } from "./lane";
import type { Filter } from "./processing-toolbar";
import {
  useFinishedAnnouncement,
  useFinishedFiles,
} from "./use-finished-files";

/** Five one-line rows keep the lane inside the board's height; History → has the rest. */
const FINISHED_SHOWN = 5;

export function FinishedLane({
  filter,
  now,
  onOpen,
}: {
  filter: Filter;
  now: number;
  onOpen: (item: FinishedFile) => void;
}) {
  const finished = useFinishedFiles();
  const announcement = useFinishedAnnouncement(finished);
  const shown = finished
    .filter((item) => filter === "all" || filter === item.source)
    .slice(0, FINISHED_SHOWN);
  return (
    <Lane
      id="finished"
      label="Just finished"
      count={null}
      hint="Open one to see exactly what Weir did"
      aside={
        <Link className="mm-live-lane__link" to="/history">
          History →
        </Link>
      }
    >
      {shown.length ? (
        <ul className="mm-live-lane__body mm-live-lane__body--list">
          {shown.map((item) => (
            <FinishedRow key={item.id} item={item} now={now} onOpen={onOpen} />
          ))}
        </ul>
      ) : (
        <EmptyLane>Nothing has finished recently.</EmptyLane>
      )}
      <p
        className="sr-only"
        role="status"
        aria-live="polite"
        data-testid="live-finished-announcement"
      >
        {announcement}
      </p>
    </Lane>
  );
}
