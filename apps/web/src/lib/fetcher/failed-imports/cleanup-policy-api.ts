import { fetchCsrfToken } from "../../api/auth-api";
import { apiFetch, readJson } from "../../api/client";
import type { FetcherFailedImportCleanupPolicyOut, FetcherFailedImportCleanupPolicyPutBody } from "./types";

export const failedImportCleanupPolicyPath = () => "/api/v1/fetcher/failed-imports/cleanup-policy";

export async function fetchFailedImportCleanupPolicy(): Promise<FetcherFailedImportCleanupPolicyOut> {
  const r = await apiFetch(failedImportCleanupPolicyPath());
  if (!r.ok) {
    throw new Error(`Could not load failed-import removal rules (${r.status})`);
  }
  return readJson<FetcherFailedImportCleanupPolicyOut>(r);
}

export async function putFailedImportCleanupPolicy(
  body: FetcherFailedImportCleanupPolicyPutBody,
): Promise<FetcherFailedImportCleanupPolicyOut> {
  const csrf_token = await fetchCsrfToken();
  const r = await apiFetch(failedImportCleanupPolicyPath(), {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ ...body, csrf_token }),
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
    throw new Error(detail || `Could not save removal rules (${r.status})`);
  }
  return readJson<FetcherFailedImportCleanupPolicyOut>(r);
}
