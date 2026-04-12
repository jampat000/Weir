import { useState } from "react";
import { PageLoading } from "../../components/shared/page-loading";
import { isHttpErrorFromApi, isLikelyNetworkFailure } from "../../lib/api/error-guards";
import { useMeQuery } from "../../lib/auth/queries";
import type { RefinerJobsInspectionFilter } from "../../lib/refiner/jobs-inspection/queries";
import {
  useRefinerJobCancelPendingMutation,
  useRefinerJobsInspectionQuery,
} from "../../lib/refiner/jobs-inspection/queries";
import type { RefinerJobInspectionRow } from "../../lib/refiner/jobs-inspection/types";

function canCancelRefinerJobs(role: string | undefined): boolean {
  return role === "operator" || role === "admin";
}

function statusLabel(status: string): string {
  if (status === "handler_ok_finalize_failed") {
    return "finalize failed";
  }
  return status;
}

/** Read ``refiner_jobs`` lifecycle here; finished outcomes stay on Activity. */
export function RefinerJobsInspectionSection() {
  const me = useMeQuery();
  const [filter, setFilter] = useState<RefinerJobsInspectionFilter>("recent");
  const q = useRefinerJobsInspectionQuery(filter);
  const cancel = useRefinerJobCancelPendingMutation();
  const canCancel = canCancelRefinerJobs(me.data?.role);

  if (q.isPending || me.isPending) {
    return <PageLoading label="Loading Refiner jobs" />;
  }
  if (q.isError) {
    return (
      <div
        className="mt-6 max-w-4xl rounded border border-red-900/40 bg-red-950/20 p-4 text-sm text-red-200"
        data-testid="refiner-jobs-inspection-error"
        role="alert"
      >
        <p className="font-semibold">Could not load Refiner jobs</p>
        <p className="mt-1">
          {isLikelyNetworkFailure(q.error)
            ? "Check that the MediaMop API is running."
            : isHttpErrorFromApi(q.error)
              ? "Sign in, then try again."
              : "Request failed."}
        </p>
      </div>
    );
  }

  const jobs = q.data?.jobs ?? [];

  return (
    <section
      className="mt-6 max-w-4xl rounded border border-[var(--mm-border)] bg-[var(--mm-card-bg)] p-4 text-sm leading-relaxed text-[var(--mm-text2)]"
      aria-labelledby="refiner-jobs-inspection-heading"
      data-testid="refiner-jobs-inspection-section"
    >
      <h2 id="refiner-jobs-inspection-heading" className="text-base font-semibold text-[var(--mm-text)]">
        Refiner jobs (queue)
      </h2>
      <p className="mt-2">
        This table reads the <strong className="text-[var(--mm-text)]">refiner_jobs</strong> table only: status,
        lease, attempts, and errors. It answers “what is queued or running?” — not full remux probe lines or scan
        summaries. For finished work, use <strong className="text-[var(--mm-text)]">Overview → Activity</strong> (and
        per-family controls above).
      </p>
      <p className="mt-2 text-[var(--mm-text3)]">
        Operators and admins may <strong className="text-[var(--mm-text)]">cancel pending</strong> rows only (never
        leased, never already finished). Cancelling frees the original dedupe key so you can enqueue again.
      </p>

      <div className="mt-4">
        <label className="block text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">Filter</label>
        <select
          className="mt-1 w-full max-w-md rounded border border-[var(--mm-border)] bg-[var(--mm-input-bg)] px-2 py-1.5 text-sm text-[var(--mm-text)]"
          value={filter}
          onChange={(e) => setFilter(e.target.value as RefinerJobsInspectionFilter)}
          data-testid="refiner-jobs-inspection-filter"
        >
          <option value="recent">Recent (all statuses, newest first)</option>
          <option value="pending">Pending only</option>
          <option value="leased">Leased only</option>
          <option value="terminal">Terminal (completed, failed, finalize-failed)</option>
          <option value="cancelled">Cancelled only</option>
          <option value="completed">Completed only</option>
          <option value="failed">Failed only</option>
          <option value="handler_ok_finalize_failed">Finalize-failed only</option>
        </select>
      </div>

      {cancel.isError ? (
        <p className="mt-3 text-sm text-red-300" role="alert" data-testid="refiner-jobs-inspection-cancel-error">
          {cancel.error instanceof Error ? cancel.error.message : "Cancel failed."}
        </p>
      ) : null}

      {jobs.length === 0 ? (
        <p className="mt-4 text-[var(--mm-text3)]" data-testid="refiner-jobs-inspection-empty">
          No rows for this filter.
        </p>
      ) : (
        <div className="mt-4 overflow-x-auto">
          <table className="w-full min-w-[44rem] border-collapse text-left text-xs">
            <thead>
              <tr className="border-b border-[var(--mm-border)] text-[var(--mm-text3)]">
                <th className="py-2 pr-2 font-semibold">ID</th>
                <th className="py-2 pr-2 font-semibold">Status</th>
                <th className="py-2 pr-2 font-semibold">Kind</th>
                <th className="py-2 pr-2 font-semibold">Updated</th>
                <th className="py-2 pr-2 font-semibold">Lease</th>
                <th className="py-2 pr-2 font-semibold">Dedupe</th>
                <th className="py-2 pr-0 font-semibold"> </th>
              </tr>
            </thead>
            <tbody>
              {jobs.map((j) => (
                <RefinerJobRow key={j.id} job={j} canCancel={canCancel} cancelMutation={cancel} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

function RefinerJobRow({
  job,
  canCancel,
  cancelMutation,
}: {
  job: RefinerJobInspectionRow;
  canCancel: boolean;
  cancelMutation: ReturnType<typeof useRefinerJobCancelPendingMutation>;
}) {
  const showCancel = canCancel && job.status === "pending";
  return (
    <tr className="border-b border-[var(--mm-border)] align-top text-[var(--mm-text)]" data-testid="refiner-jobs-row">
      <td className="py-2 pr-2 font-mono">{job.id}</td>
      <td className="py-2 pr-2 whitespace-nowrap">{statusLabel(job.status)}</td>
      <td className="py-2 pr-2 font-mono text-[0.8rem] text-[var(--mm-text2)]">{job.job_kind}</td>
      <td className="py-2 pr-2 whitespace-nowrap text-[var(--mm-text2)]">{job.updated_at}</td>
      <td className="py-2 pr-2 text-[var(--mm-text2)]">
        {job.lease_owner ? (
          <span title={job.lease_expires_at ?? ""}>
            {job.lease_owner}
            {job.lease_expires_at ? ` · ${job.lease_expires_at}` : ""}
          </span>
        ) : (
          "—"
        )}
      </td>
      <td className="max-w-[14rem] py-2 pr-2 break-words font-mono text-[0.75rem] text-[var(--mm-text3)]">
        <span title={job.dedupe_key}>{job.dedupe_key.length > 48 ? `${job.dedupe_key.slice(0, 48)}…` : job.dedupe_key}</span>
      </td>
      <td className="py-2 pr-0 text-right">
        {showCancel ? (
          <button
            type="button"
            className="rounded border border-[var(--mm-border)] px-2 py-1 text-xs text-[var(--mm-text)] hover:bg-black/20 disabled:opacity-50"
            disabled={cancelMutation.isPending}
            data-testid={`refiner-jobs-cancel-${job.id}`}
            onClick={() => cancelMutation.mutate(job.id)}
          >
            Cancel pending
          </button>
        ) : null}
      </td>
    </tr>
  );
}
