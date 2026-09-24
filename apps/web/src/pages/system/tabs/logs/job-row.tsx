import type {
  useProcessingJobCancelPendingMutation,
  useProcessingJobRecoverFinalizeFailedMutation,
} from "../../../../lib/processing/jobs-inspection/queries";
import type { ProcessingJobInspectionRow } from "../../../../lib/processing/jobs-inspection/types";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { jobKindLabel, statusLabel, technicalJobSummary } from "./jobs-labels";

function JobActions({
  job,
  canAct,
  cancel,
  recover,
}: {
  job: ProcessingJobInspectionRow;
  canAct: boolean;
  cancel: ReturnType<typeof useProcessingJobCancelPendingMutation>;
  recover: ReturnType<typeof useProcessingJobRecoverFinalizeFailedMutation>;
}) {
  if (!canAct) return null;
  if (job.status === "pending") {
    return (
      <button
        type="button"
        className={mmActionButtonClass({ variant: "tertiary" })}
        disabled={cancel.isPending}
        data-testid={`processing-jobs-cancel-${job.id}`}
        onClick={() => cancel.mutate(job.id)}
      >
        Cancel pending
      </button>
    );
  }
  if (job.status === "handler_ok_finalize_failed") {
    return (
      <button
        type="button"
        className={mmActionButtonClass({ variant: "secondary" })}
        disabled={recover.isPending}
        data-testid={`processing-jobs-recover-${job.id}`}
        onClick={() => recover.mutate(job.id)}
      >
        {recover.isPending ? "Recovering…" : "Recover result"}
      </button>
    );
  }
  return null;
}

export function JobRow({
  job,
  canAct,
  cancel,
  recover,
  formatDate,
  processingPaused,
}: {
  job: ProcessingJobInspectionRow;
  canAct: boolean;
  cancel: ReturnType<typeof useProcessingJobCancelPendingMutation>;
  recover: ReturnType<typeof useProcessingJobRecoverFinalizeFailedMutation>;
  formatDate: (iso: string) => string;
  processingPaused: boolean;
}) {
  const pausedPending = processingPaused && job.status === "pending";
  const detail = job.technical_detail || job.last_error;
  return (
    <tr data-testid="processing-jobs-row">
      <th
        scope="row"
        className="mm-quiet-table__name mm-jobs-table__job left-0 z-1 max-w-64 bg-mm-bg-main pr-4"
      >
        <span className="block">{jobKindLabel(job.job_kind)}</span>
        <span className="mm-quiet-table__sub font-mono">Job #{job.id}</span>
      </th>
      <td data-label="Status" className="whitespace-nowrap">
        {statusLabel(job.status)}
      </td>
      <td
        data-label="Updated"
        className="whitespace-nowrap text-xs text-mm-text2"
      >
        {formatDate(job.updated_at)}
      </td>
      <td data-label="What happened" className="min-w-76 max-w-md break-words">
        <p className="text-sm text-mm-text1">
          {pausedPending
            ? "This job is safely waiting because Weir is paused."
            : job.operator_message || "This job needs a review."}
        </p>
        <p className="mt-1 text-xs text-mm-text3">
          <span className="font-semibold text-mm-text2">Next step:</span>{" "}
          {pausedPending
            ? "No action is required. Use Resume at the top of the page when you want queued work to continue."
            : job.next_action || "Open the related screen to inspect the job."}
        </p>
        <details className="mt-2 text-xs text-mm-text3">
          <summary className="cursor-pointer select-none">
            Technical details
          </summary>
          {/* Verbatim diagnostic text: the box is what marks it as raw output. */}
          <pre className="mt-1 max-h-36 overflow-auto whitespace-pre-wrap break-words rounded border border-mm-border bg-mm-well-bg p-2">
            {technicalJobSummary(job)}
            {detail ? `\n\n${detail}` : ""}
          </pre>
        </details>
      </td>
      <td data-label="Action" className="text-right">
        <JobActions
          job={job}
          canAct={canAct}
          cancel={cancel}
          recover={recover}
        />
      </td>
    </tr>
  );
}
