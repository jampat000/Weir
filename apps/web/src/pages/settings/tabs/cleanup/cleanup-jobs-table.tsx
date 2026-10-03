import { Fragment, type ReactNode } from "react";

import { SortableColumnHeader } from "../../../../components/shared/sortable-column-header";
import type { MaintenanceFamilyState } from "../../../../lib/processing/maintenance-api";
import { parseAppTime } from "../../../../lib/ui/mm-format-date";
import { sortRows } from "../../../../lib/ui/table-columns";
import type { TableColumns } from "../../../../lib/ui/use-table-columns";
import type { CleanupColumnId } from "./cleanup-columns";
import {
  CLEANUP_JOBS,
  DEFAULT_INTERVAL_SECONDS,
  type CleanupJob,
} from "./cleanup-jobs";

type JobEntry = { job: CleanupJob; state: MaintenanceFamilyState };

/** What each column that sorts sorts by. A job with no time to show is last either way. */
const JOB_SORT_VALUES = {
  job: ({ job }: JobEntry) => job.name,
  on: ({ state }: JobEntry) => Number(state.enabled),
  every: ({ state }: JobEntry) =>
    state.interval_seconds ?? DEFAULT_INTERVAL_SECONDS,
  lastRun: ({ state }: JobEntry) => parseAppTime(state.last_completed_at),
  nextRun: ({ state }: JobEntry) =>
    state.enabled ? parseAppTime(state.next_run_at) : null,
};

/**
 * The cleanup jobs under headings that sort them and whose columns move. Each job is given its own row, followed by
 * whatever `after` makes for it, so a setting that belongs to a job stays under it; `footer` follows the last job.
 */
export function CleanupJobsTable({
  columns,
  shown,
  states,
  row,
  after,
  footer,
}: {
  columns: TableColumns<CleanupColumnId>;
  /** The columns to draw, in order: those of `columns`, less the one that is not offered to a reader. */
  shown: readonly CleanupColumnId[];
  states: readonly MaintenanceFamilyState[];
  row: (entry: JobEntry) => ReactNode;
  after: (job: CleanupJob) => ReactNode;
  footer: ReactNode;
}) {
  const entries = CLEANUP_JOBS.flatMap((job) => {
    const state = states.find((candidate) => candidate.family === job.family);
    return state ? [{ job, state }] : [];
  });
  return (
    <div className="mm-quiet-table-wrap">
      <table className="mm-quiet-table" {...columns.tableProps}>
        <thead>
          <tr>
            {shown.map((id) => (
              <SortableColumnHeader
                key={id}
                heading={columns.heading(id)}
                hideLabel={id === "runNow"}
              />
            ))}
          </tr>
        </thead>
        <tbody>
          {sortRows(entries, columns.sort, JOB_SORT_VALUES).map((entry) => (
            <Fragment key={entry.job.family}>
              {row(entry)}
              {after(entry.job)}
            </Fragment>
          ))}
          {footer}
        </tbody>
      </table>
    </div>
  );
}
