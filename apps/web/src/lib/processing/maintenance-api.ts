import { sendJson } from "../api/send-json";
import { apiFetch, readJson, requireOk } from "../api/client";

export type MaintenanceFamily =
  "work_temp_stale_sweep" | "failure_cleanup" | "unclaimed_handbacks";

export interface MaintenanceFamilyState {
  family: MaintenanceFamily;
  /** Whether the schedule runs this. Triggering by hand ignores it. */
  enabled: boolean;
  description: string;
  pending: number;
  running: number;
  last_completed_at: string | null;
  last_failed_at: string | null;
  last_error: string | null;
  /** How often it runs: the interval saved in Settings › Cleanup, else the environment's. */
  interval_seconds?: number;
  /** When it next runs by itself; null while it is switched off. */
  next_run_at?: string | null;
  /** Unclaimed hand-backs only: how many days a copy waits before the job may remove it. */
  window_days?: number;
}

export interface MaintenanceState {
  families: MaintenanceFamilyState[];
}

export interface MaintenanceTriggerResult {
  queued: boolean;
  detail: string;
  job_id: number | null;
}

export const processingMaintenancePath = () => "/api/v1/processing/maintenance";

export async function fetchProcessingMaintenance(): Promise<MaintenanceState> {
  const path = processingMaintenancePath();
  const response = await apiFetch(path);
  await requireOk(path, response, "Could not read the maintenance state");
  return readJson<MaintenanceState>(response);
}

export async function runProcessingMaintenance(
  family: MaintenanceFamily,
  media_scope: "movie" | "tv",
): Promise<MaintenanceTriggerResult> {
  const path = `${processingMaintenancePath()}/run`;
  const response = await sendJson(
    path,
    "POST",
    { family, media_scope },
    "Could not start that maintenance job",
  );
  return readJson<MaintenanceTriggerResult>(response);
}
