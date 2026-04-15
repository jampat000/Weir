import { fetchCsrfToken } from "../../api/auth-api";
import { apiFetch, readJson } from "../../api/client";
import type {
  FailedImportCleanupPolicyAxis,
  FetcherFailedImportCleanupPolicyOut,
  FetcherFailedImportCleanupPolicyPutBody,
} from "./types";

export const failedImportCleanupPolicyPath = () => "/api/v1/fetcher/failed-imports/cleanup-policy";

export async function fetchFailedImportCleanupPolicy(): Promise<FetcherFailedImportCleanupPolicyOut> {
  const r = await apiFetch(failedImportCleanupPolicyPath());
  if (!r.ok) {
    throw new Error(`Could not load failed-import removal rules (${r.status})`);
  }
  return readJson<FetcherFailedImportCleanupPolicyOut>(r);
}

async function putFailedImportCleanupPolicyAxis(
  pathSuffix: "tv-shows" | "movies",
  axis: FailedImportCleanupPolicyAxis,
): Promise<FetcherFailedImportCleanupPolicyOut> {
  const csrf_token = await fetchCsrfToken();
  const r = await apiFetch(`${failedImportCleanupPolicyPath()}/${pathSuffix}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ ...axis, csrf_token }),
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

export async function putFailedImportCleanupPolicyTvShows(
  axis: FailedImportCleanupPolicyAxis,
): Promise<FetcherFailedImportCleanupPolicyOut> {
  return putFailedImportCleanupPolicyAxis("tv-shows", axis);
}

export async function putFailedImportCleanupPolicyMovies(
  axis: FailedImportCleanupPolicyAxis,
): Promise<FetcherFailedImportCleanupPolicyOut> {
  return putFailedImportCleanupPolicyAxis("movies", axis);
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
