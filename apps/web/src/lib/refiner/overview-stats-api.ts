import { apiFetch, readJson } from "../api/client";
import type { RefinerOverviewStatsOut } from "./types";

export const refinerOverviewStatsPath = () => "/api/v1/refiner/overview-stats";

export async function fetchRefinerOverviewStats(): Promise<RefinerOverviewStatsOut> {
  const r = await apiFetch(refinerOverviewStatsPath());
  if (!r.ok) {
    throw new Error(`Could not load Refiner overview stats (${r.status})`);
  }
  return readJson<RefinerOverviewStatsOut>(r);
}
