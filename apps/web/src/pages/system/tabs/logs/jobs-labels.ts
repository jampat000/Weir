import type { ProcessingJobsInspectionFilter } from "../../../../lib/processing/jobs-inspection/queries";
import type { ProcessingJobInspectionRow } from "../../../../lib/processing/jobs-inspection/types";

const STATUS_LABELS: Record<string, string> = {
  pending: "Queued",
  leased: "Running",
  completed: "Finished",
  failed: "Failed",
  cancelled: "Cancelled",
  handler_ok_finalize_failed: "Recovery needed",
};

const JOB_KIND_LABELS: Record<string, string> = {
  "processing.watched_folder.remux_scan_dispatch.v1": "Check watched folders",
  "processing.candidate_gate.v1": "Check file readiness",
  "processing.supplied_payload_evaluation.v1": "Check a manually supplied file",
  "processing.file.remux_pass.v1": "Process media file",
  "processing.work_temp_stale_sweep.v1": "Clean temporary work files",
  "processing.failure_cleanup.v1": "Clean failed work files",
  "processing.unclaimed_handback_cleanup.v1": "Remove copies nobody picked up",
};

export function statusLabel(status: string): string {
  return STATUS_LABELS[status] ?? status;
}

/** A job kind in words; one without its own label reads as the kind's second-to-last part. */
export function jobKindLabel(jobKind: string): string {
  const known = JOB_KIND_LABELS[jobKind];
  if (known) return known;
  const last = jobKind.split(".").filter(Boolean).at(-2) ?? jobKind;
  return last
    .replaceAll("_", " ")
    .replace(/^./, (value) => value.toUpperCase());
}

export function technicalJobSummary(job: ProcessingJobInspectionRow): string {
  const lines = [
    `Internal kind: ${job.job_kind}`,
    `Dedupe key: ${job.dedupe_key}`,
  ];
  if (job.lease_owner) lines.push(`Worker lease: ${job.lease_owner}`);
  if (job.lease_expires_at) lines.push(`Lease expiry: ${job.lease_expires_at}`);
  return lines.join("\n");
}

export const JOBS_FILTER_OPTIONS: {
  value: ProcessingJobsInspectionFilter;
  label: string;
}[] = [
  { value: "recent", label: "Recent work (routine successful scans hidden)" },
  { value: "pending", label: "Pending only" },
  { value: "leased", label: "Running now" },
  { value: "terminal", label: "Terminal (completed, failed, finalize-failed)" },
  { value: "cancelled", label: "Cancelled only" },
  { value: "completed", label: "Completed only" },
  { value: "failed", label: "Failed only" },
  { value: "handler_ok_finalize_failed", label: "Needs recovery" },
];

/** A filter named in the address, so a link can open the list already narrowed. "recent" is the default. */
export function filterFromUrl(
  value: string | null,
): ProcessingJobsInspectionFilter | null {
  const match = JOBS_FILTER_OPTIONS.find(
    (option) => option.value === value && option.value !== "recent",
  );
  return match?.value ?? null;
}
