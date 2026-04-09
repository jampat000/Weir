import { apiFetch, readJson } from "./client";
import type { ActivityRecentResponse } from "./types";

export async function fetchActivityRecent(): Promise<ActivityRecentResponse> {
  const r = await apiFetch("/api/v1/activity/recent");
  if (!r.ok) {
    throw new Error(`activity recent: ${r.status}`);
  }
  return readJson<ActivityRecentResponse>(r);
}
