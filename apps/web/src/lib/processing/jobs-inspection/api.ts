import { sendJson } from "../../api/send-json";
import { apiFetch, readJson, requireOk } from "../../api/client";
import type {
  ProcessingJobCancelPendingOut,
  ProcessingJobRecoverFinalizeFailedOut,
  ProcessingJobsInspectionOut,
} from "./types";

export type FetchProcessingJobsInspectionOpts = {
  statuses?: string[];
  limit?: number;
};

export function processingJobsInspectionPath(
  opts?: FetchProcessingJobsInspectionOpts,
): string {
  const params = new URLSearchParams();
  const limit = opts?.limit ?? 100;
  params.set("limit", String(limit));
  if (opts?.statuses?.length) {
    for (const s of opts.statuses) {
      params.append("status", s);
    }
  }
  return `/api/v1/processing/jobs/inspection?${params.toString()}`;
}

export async function fetchProcessingJobsInspection(
  opts?: FetchProcessingJobsInspectionOpts,
): Promise<ProcessingJobsInspectionOut> {
  const path = processingJobsInspectionPath(opts);
  const r = await apiFetch(path);
  await requireOk(path, r, "Could not load jobs");
  return readJson<ProcessingJobsInspectionOut>(r);
}

export async function postProcessingJobCancelPending(
  jobId: number,
): Promise<ProcessingJobCancelPendingOut> {
  const path = `/api/v1/processing/jobs/${jobId}/cancel-pending`;
  const r = await sendJson(path, "POST", {}, "Could not cancel job");
  return readJson<ProcessingJobCancelPendingOut>(r);
}

export async function postProcessingJobRecoverFinalizeFailed(
  jobId: number,
): Promise<ProcessingJobRecoverFinalizeFailedOut> {
  const path = `/api/v1/processing/jobs/${jobId}/recover-finalize-failed`;
  const r = await sendJson(path, "POST", {}, "Could not recover that result");
  return readJson<ProcessingJobRecoverFinalizeFailedOut>(r);
}
