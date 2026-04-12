import { fetchCsrfToken } from "../../api/auth-api";
import { apiFetch, readJson } from "../../api/client";
import type { RefinerJobCancelPendingOut, RefinerJobsInspectionOut } from "./types";

export type FetchRefinerJobsInspectionOpts = {
  statuses?: string[];
  limit?: number;
};

export function refinerJobsInspectionPath(opts?: FetchRefinerJobsInspectionOpts): string {
  const params = new URLSearchParams();
  const limit = opts?.limit ?? 50;
  params.set("limit", String(limit));
  if (opts?.statuses?.length) {
    for (const s of opts.statuses) {
      params.append("status", s);
    }
  }
  return `/api/v1/refiner/jobs/inspection?${params.toString()}`;
}

export async function fetchRefinerJobsInspection(
  opts?: FetchRefinerJobsInspectionOpts,
): Promise<RefinerJobsInspectionOut> {
  const r = await apiFetch(refinerJobsInspectionPath(opts));
  if (!r.ok) {
    throw new Error(`Could not load Refiner jobs (${r.status})`);
  }
  return readJson<RefinerJobsInspectionOut>(r);
}

export async function postRefinerJobCancelPending(jobId: number): Promise<RefinerJobCancelPendingOut> {
  const csrf_token = await fetchCsrfToken();
  const r = await apiFetch(`/api/v1/refiner/jobs/${jobId}/cancel-pending`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ csrf_token }),
  });
  if (!r.ok) {
    let detail = r.statusText;
    try {
      const b = await readJson<{ detail?: string }>(r);
      if (typeof b.detail === "string") {
        detail = b.detail;
      }
    } catch {
      /* ignore */
    }
    throw new Error(detail || `Could not cancel Refiner job (${r.status})`);
  }
  return readJson<RefinerJobCancelPendingOut>(r);
}
