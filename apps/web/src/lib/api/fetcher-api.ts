import { apiFetch, readJson } from "./client";
import type { FetcherOperationalOverview } from "./types";

export async function fetchFetcherOperationalOverview(): Promise<FetcherOperationalOverview> {
  const r = await apiFetch("/api/v1/fetcher/overview");
  if (!r.ok) {
    throw new Error(`fetcher overview: ${r.status}`);
  }
  return readJson<FetcherOperationalOverview>(r);
}
