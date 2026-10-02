import { apiFetch, readJson, requireOk } from "../api/client";
import type { SystemOverview, SystemStats } from "./system-stats-types";

const statsPath = "/api/v1/system/stats";
const overviewPath = "/api/v1/system/overview";

/** The last ten minutes of readings and what this computer and its drives are doing now. */
export async function fetchSystemStats(): Promise<SystemStats> {
  const response = await apiFetch(statsPath);
  await requireOk(statsPath, response, "Could not read this computer");
  return readJson<SystemStats>(response);
}

/** How Weir itself is running: its version, uptime, address, jobs and requests. */
export async function fetchSystemOverview(): Promise<SystemOverview> {
  const response = await apiFetch(overviewPath);
  await requireOk(overviewPath, response, "Could not read how Weir is running");
  return readJson<SystemOverview>(response);
}
