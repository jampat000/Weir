/** GET/PUT /api/v1/refiner/path-settings — Refiner watched / work / output folders (singleton row). */

export type RefinerPathSettingsOut = {
  refiner_watched_folder: string | null;
  refiner_work_folder: string | null;
  refiner_output_folder: string;
  resolved_default_work_folder: string;
  effective_work_folder: string;
  updated_at: string;
};

export type RefinerPathSettingsPutBody = {
  refiner_watched_folder: string | null;
  refiner_work_folder: string | null;
  refiner_output_folder: string;
};

/** GET /api/v1/refiner/runtime-settings — read-only Refiner in-process worker snapshot. */

export type RefinerRuntimeSettingsOut = {
  in_process_refiner_worker_count: number;
  in_process_workers_disabled: boolean;
  in_process_workers_enabled: boolean;
  worker_mode_summary: string;
  sqlite_throughput_note: string;
  configuration_note: string;
  visibility_note: string;
  refiner_watched_folder_remux_scan_dispatch_schedule_enabled: boolean;
  refiner_watched_folder_remux_scan_dispatch_schedule_interval_seconds: number;
  refiner_watched_folder_remux_scan_dispatch_periodic_enqueue_remux_jobs: boolean;
  refiner_watched_folder_remux_scan_dispatch_periodic_remux_dry_run: boolean;
  watched_folder_scan_periodic_configuration_note: string;
};

/** POST /api/v1/refiner/jobs/watched-folder-remux-scan-dispatch/enqueue */

export type RefinerWatchedFolderRemuxScanDispatchEnqueueBody = {
  enqueue_remux_jobs: boolean;
  remux_dry_run: boolean;
};

export type RefinerWatchedFolderRemuxScanDispatchEnqueueOut = {
  ok: boolean;
  job_id: number;
  dedupe_key: string;
  job_kind: string;
};
