import { useId, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";

import { MmJobsPagination } from "../../../../components/overview/mm-overview-cards";
import { MmListboxPicker } from "../../../../components/ui/mm-listbox-picker";
import { errorMessage } from "../../../../lib/api/error-message";
import {
  isHttpErrorFromApi,
  isLikelyNetworkFailure,
} from "../../../../lib/api/error-guards";
import { canEdit } from "../../../../lib/auth/can-edit";
import { useMeQuery } from "../../../../lib/auth/queries";
import { usePauseQuery } from "../../../../lib/pause/pause-queries";
import {
  useProcessingJobCancelPendingMutation,
  useProcessingJobRecoverFinalizeFailedMutation,
  useProcessingJobsInspectionQuery,
  type ProcessingJobsInspectionFilter,
} from "../../../../lib/processing/jobs-inspection/queries";
import type { ProcessingJobInspectionRow } from "../../../../lib/processing/jobs-inspection/types";
import { useAppDateFormatter } from "../../../../lib/ui/mm-format-date";
import { JobRow } from "./job-row";
import { JOBS_FILTER_OPTIONS, filterFromUrl } from "./jobs-labels";

const PAGE_SIZE_OPTIONS = [20, 50, 100];
const HEAD_CELL = "mm-jobs-table__head top-0 z-20 bg-mm-bg-main";

function FailedLine({ text, testId }: { text: string; testId: string }) {
  return (
    <p
      className="text-sm text-mm-status-failed-text"
      role="alert"
      data-testid={testId}
    >
      {text}
    </p>
  );
}

function loadErrorWords(error: unknown): string {
  if (isLikelyNetworkFailure(error)) {
    return "Could not reach the Weir API. Check that the backend is running.";
  }
  if (isHttpErrorFromApi(error)) {
    return "The server refused this request. Sign in again, then try this page.";
  }
  return errorMessage(error, "Could not load jobs.");
}

/**
 * The jobs table. The scroll wrapper lets a wide table live in a narrow panel and is the scrollport
 * the sticky Job column and header pin to; the sticky cells sit on the page background so rows do
 * not show through them. Both stop where .mm-quiet-table stacks into rows (weir-content.css).
 */
function JobsTable({
  jobs,
  canAct,
  processingPaused,
  cancel,
  recover,
}: {
  jobs: ProcessingJobInspectionRow[];
  canAct: boolean;
  processingPaused: boolean;
  cancel: ReturnType<typeof useProcessingJobCancelPendingMutation>;
  recover: ReturnType<typeof useProcessingJobRecoverFinalizeFailedMutation>;
}) {
  const formatDate = useAppDateFormatter();
  return (
    <div className="mm-quiet-table-wrap w-full min-w-0">
      <table className="mm-quiet-table mm-jobs-table">
        <thead>
          <tr>
            <th scope="col" className={`${HEAD_CELL} left-0 z-30 pr-4`}>
              Job
            </th>
            <th scope="col" className={HEAD_CELL}>
              Status
            </th>
            <th scope="col" className={HEAD_CELL}>
              Updated
            </th>
            <th scope="col" className={HEAD_CELL}>
              What happened and what to do
            </th>
            <th scope="col" className={HEAD_CELL}>
              <span className="sr-only">Action</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {jobs.map((job) => (
            <JobRow
              key={job.id}
              job={job}
              canAct={canAct}
              cancel={cancel}
              recover={recover}
              formatDate={formatDate}
              processingPaused={processingPaused}
            />
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** The job queue as it stands; finished outcomes stay on Activity. */
export function JobsSection() {
  const me = useMeQuery();
  const pause = usePauseQuery();
  const [searchParams] = useSearchParams();
  const filterLabelId = useId();
  const urlFilter = filterFromUrl(searchParams.get("status"));
  const [filter, setFilter] = useState<ProcessingJobsInspectionFilter>(
    () => urlFilter ?? "recent",
  );
  const [seenUrlFilter, setSeenUrlFilter] = useState(urlFilter);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(PAGE_SIZE_OPTIONS[0]);
  const q = useProcessingJobsInspectionQuery(filter);
  const cancel = useProcessingJobCancelPendingMutation();
  const recover = useProcessingJobRecoverFinalizeFailedMutation();

  // A link that names a status narrows the list, even when the tab is already open.
  if (urlFilter !== seenUrlFilter) {
    setSeenUrlFilter(urlFilter);
    if (urlFilter) {
      setFilter(urlFilter);
      setPage(1);
    }
  }

  const jobs = q.data?.jobs ?? [];
  const totalPages = Math.max(1, Math.ceil(jobs.length / pageSize));
  const shownPage = Math.min(page, totalPages);
  const pagedRows = jobs.slice(
    (shownPage - 1) * pageSize,
    shownPage * pageSize,
  );

  return (
    <section
      className="mm-quiet-stack"
      aria-labelledby="processing-jobs-inspection-heading"
      data-testid="processing-jobs-inspection-section"
    >
      <div className="mm-quiet-section">
        <div className="mm-quiet-section__head">
          <h2
            id="processing-jobs-inspection-heading"
            className="mm-quiet-section__title"
          >
            Jobs
          </h2>
        </div>
        <div className="mm-quiet-section__body space-y-4">
          <p className="mm-quiet-note">
            Current and recent work, with a clear next step when you need to
            act.
          </p>
          <label className="block min-w-0 max-w-xl">
            <span id={filterLabelId} className="text-sm text-mm-text2">
              Show jobs
            </span>
            <MmListboxPicker
              className="mt-2"
              data-testid="processing-jobs-inspection-filter"
              ariaLabelledBy={filterLabelId}
              placeholder="Select filter"
              options={JOBS_FILTER_OPTIONS}
              value={filter}
              onChange={(value) => {
                setFilter(value as ProcessingJobsInspectionFilter);
                setPage(1);
              }}
            />
          </label>
          {q.isPending || me.isPending ? (
            <p className="text-sm text-mm-text2">Loading jobs…</p>
          ) : null}
          {q.isError ? (
            <FailedLine
              text={loadErrorWords(q.error)}
              testId="processing-jobs-inspection-error"
            />
          ) : null}
          {cancel.isError ? (
            <FailedLine
              text={errorMessage(cancel.error, "Cancel failed.")}
              testId="processing-jobs-inspection-cancel-error"
            />
          ) : null}
          {recover.isError ? (
            <FailedLine
              text={errorMessage(recover.error, "Recovery failed.")}
              testId="processing-jobs-inspection-recover-error"
            />
          ) : null}

          {!q.isPending && !q.isError && jobs.length === 0 ? (
            <div
              className="py-6"
              data-testid="processing-jobs-inspection-empty"
            >
              <p className="text-sm font-medium text-mm-text1">
                No jobs match this view
              </p>
              <p className="mt-1 text-xs text-mm-text2">
                Nothing matches this filter yet. Try{" "}
                <strong className="text-mm-text2">Recent work</strong> for the
                latest rows.
              </p>
            </div>
          ) : null}

          {!q.isPending && !q.isError && jobs.length > 0 ? (
            <>
              <JobsTable
                jobs={pagedRows}
                canAct={canEdit(me.data?.role)}
                processingPaused={pause.data?.paused === true}
                cancel={cancel}
                recover={recover}
              />
              <MmJobsPagination
                page={shownPage}
                totalPages={totalPages}
                onPageChange={setPage}
                pageSize={pageSize}
                onPageSizeChange={(size) => {
                  setPageSize(size);
                  setPage(1);
                }}
                pageSizeOptions={PAGE_SIZE_OPTIONS}
              />
            </>
          ) : null}

          <p className="text-xs text-mm-text2">
            Full detail on each outcome is in the{" "}
            <Link to="/system?tab=history" className="text-mm-accent underline">
              Activity log
            </Link>
            .
          </p>
        </div>
      </div>
    </section>
  );
}
