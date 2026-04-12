import { fetchCsrfToken } from "../../api/auth-api";
import { apiFetch, readJson } from "../../api/client";

export type FetcherJobRecoverFinalizeResult = {
  job_id: number;
  status: string;
};

/** Manual ``handler_ok_finalize_failed`` → ``completed`` for any ``fetcher_jobs`` row (Fetcher lane). */
export async function postFetcherJobRecoverFinalize(jobId: number): Promise<FetcherJobRecoverFinalizeResult> {
  const csrf_token = await fetchCsrfToken();
  const r = await apiFetch(
    `/api/v1/fetcher/jobs/${jobId}/recover-finalize-failure`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ confirm: true, csrf_token }),
    },
  );
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
    throw new Error(detail || `Could not apply manual completion (${r.status})`);
  }
  return readJson<FetcherJobRecoverFinalizeResult>(r);
}
