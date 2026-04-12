/** GET /api/v1/refiner/runtime-settings — read-only Refiner in-process worker snapshot. */

export type RefinerRuntimeSettingsOut = {
  in_process_refiner_worker_count: number;
  in_process_workers_disabled: boolean;
  in_process_workers_enabled: boolean;
  worker_mode_summary: string;
  sqlite_throughput_note: string;
  configuration_note: string;
  visibility_note: string;
};
