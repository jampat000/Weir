import { useMemo } from "react";
import { Link } from "react-router-dom";

import {
  LIBRARY_FILE_CLEANED_EVENT,
  REMUX_PASS_COMPLETED_EVENT,
} from "../../lib/activity/event-types";
import {
  finishedFileFromEvent,
  type FinishedFile,
} from "../../lib/activity/processing-outcome";
import { useActivityRecentQuery } from "../../lib/activity/queries";
import { FinishedRow } from "./lane-cards";
import { EmptyLane, Lane } from "./lane";
import type { Filter } from "./processing-toolbar";

const RECENT_PASSES = 24;
const RECENT_CLEANS = 12;
/** Five one-line rows keep the lane inside the board's height; History → has the rest. */
const FINISHED_SHOWN = 5;

/** The newest finished files of both kinds, from the Activity entry each one wrote. */
function useFinishedFiles(): FinishedFile[] {
  const passes = useActivityRecentQuery({
    limit: RECENT_PASSES,
    event_type: REMUX_PASS_COMPLETED_EVENT,
  });
  const cleans = useActivityRecentQuery({
    limit: RECENT_CLEANS,
    event_type: LIBRARY_FILE_CLEANED_EVENT,
  });
  return useMemo(() => {
    const all = [...(passes.data?.items ?? []), ...(cleans.data?.items ?? [])]
      .map(finishedFileFromEvent)
      .filter((item): item is FinishedFile => item !== null);
    return all.sort((a, b) => b.finishedAt.localeCompare(a.finishedAt));
  }, [passes.data, cleans.data]);
}

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
        <Link className="mm-live-lane__link" to="/system?tab=history">
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
    </Lane>
  );
}
