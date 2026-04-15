import { useId, useState } from "react";
import { PageLoading } from "../../components/shared/page-loading";
import { isHttpErrorFromApi, isLikelyNetworkFailure } from "../../lib/api/error-guards";
import { useMeQuery } from "../../lib/auth/queries";
import type { RefinerJobsInspectionFilter } from "../../lib/refiner/jobs-inspection/queries";
import {
  useRefinerJobCancelPendingMutation,
  useRefinerJobsInspectionQuery,
} from "../../lib/refiner/jobs-inspection/queries";
import type { RefinerJobInspectionRow } from "../../lib/refiner/jobs-inspection/types";
import { MmListboxPicker } from "../../components/ui/mm-listbox-picker";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";

function canCancelRefinerJobs(role: string | undefined): boolean {
  return role === "operator" || role === "admin";
}

function statusLabel(status: string): string {
  if (status === "handler_ok_finalize_failed") {
    return "finalize failed";
  }
  return status;
}

const REFINER_JOBS_INSPECTION_FILTER_OPTIONS: { value: RefinerJobsInspectionFilter; label: string }[] = [
  { value: "recent", label: "Recent (all statuses, newest first)" },
  { value: "pending", label: "Pending only" },
  { value: "leased", label: "Leased only" },
  { value: "terminal", label: "Terminal (completed, failed, finalize-failed)" },
  { value: "cancelled", label: "Cancelled only" },
  { value: "completed", label: "Completed only" },
  { value: "failed", label: "Failed only" },
  { value: "handler_ok_finalize_failed", label: "Finalize-failed only" },
];

/** Read ``refiner_jobs`` lifecycle here; finished outcomes stay on Activity. */
export function RefinerJobsInspectionSection() {
  const me = useMeQuery();
  const filterLabelId = useId();
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
        className="mm-fetcher-module-surface w-full min-w-0 rounded border border-red-900/40 bg-red-950/20 p-4 text-sm text-red-200"
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
      className="mm-fetcher-module-surface w-full min-w-0 rounded border border-[var(--mm-border)] bg-[var(--mm-card-bg)] p-5 text-sm leading-relaxed text-[var(--mm-text2)] sm:p-6"
      aria-labelledby="refiner-jobs-inspection-heading"
      data-testid="refiner-jobs-inspection-section"
    >
      <h2 id="refiner-jobs-inspection-heading" className="text-base font-semibold text-[var(--mm-text)]">
        Jobs
      </h2>
      <p className="mt-2 max-w-3xl">
        Use this queue view to see what Refiner is waiting on, processing now, or recently finished on this server. Open{" "}
        <strong className="text-[var(--mm-text)]">Activity</strong> for full run detail once a job completes.
      </p>
      <p className="mt-2 text-xs text-[var(--mm-text3)]">
        Operators and admins: <strong className="text-[var(--mm-text)]">Cancel pending</strong> only (not leased or
        terminal rows).
      </p>

      <div className="mt-5">
        <label className="block">
          <span id={filterLabelId} className="text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">
            Filter
          </span>
          <MmListboxPicker
            className="max-w-md"
            data-testid="refiner-jobs-inspection-filter"
            ariaLabelledBy={filterLabelId}
            placeholder="Select filter"
            options={REFINER_JOBS_INSPECTION_FILTER_OPTIONS}
            value={filter}
            onChange={(v) => setFilter(v as RefinerJobsInspectionFilter)}
          />
        </label>
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
        <div className="mt-4 w-full min-w-0 overflow-x-auto">
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
            className={mmActionButtonClass({
              variant: "tertiary",
              disabled: cancelMutation.isPending,
            })}
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
