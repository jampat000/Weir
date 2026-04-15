/** Shapes for Fetcher failed-import queue workflow APIs (JSON field names follow backend Pydantic models). */

/** GET /api/v1/fetcher/failed-imports/settings — settings snapshot from loaded config (not liveness). */
export type FailedImportFetcherSettingsOut = {
  background_job_worker_count: number;
  in_process_workers_disabled: boolean;
  in_process_workers_enabled: boolean;
  worker_mode_summary: string;
  failed_import_radarr_cleanup_drive_schedule_enabled: boolean;
  failed_import_radarr_cleanup_drive_schedule_interval_seconds: number;
  failed_import_sonarr_cleanup_drive_schedule_enabled: boolean;
  failed_import_sonarr_cleanup_drive_schedule_interval_seconds: number;
  visibility_note: string;
};

export type FailedImportManualQueuePassOut = {
  job_id: number;
  dedupe_key: string;
  job_kind: string;
  queue_outcome: "created" | "already_present";
};

export type FetcherFailedImportAxisSummary = {
  last_finished_at: string | null;
  last_outcome_label: string;
  saved_schedule_primary: string;
  saved_schedule_secondary: string | null;
};

export type FetcherFailedImportAutomationSummary = {
  scope_note: string;
  automation_slots_note: string | null;
  movies: FetcherFailedImportAxisSummary;
  tv_shows: FetcherFailedImportAxisSummary;
};

/** GET /api/v1/fetcher/failed-imports/queue-attention-snapshot — live queue scan per app. */
export type FetcherFailedImportQueueAttentionAxis = {
  needs_attention_count: number | null;
  last_checked_at: string | null;
};

export type FetcherFailedImportQueueAttentionSnapshot = {
  tv_shows: FetcherFailedImportQueueAttentionAxis;
  movies: FetcherFailedImportQueueAttentionAxis;
};

/** Per-class Sonarr/Radarr queue handling (matches ``FailedImportQueueHandlingAction`` API strings). */
export type FailedImportQueueHandlingAction =
  | "leave_alone"
  | "remove_only"
  | "blocklist_only"
  | "remove_and_blocklist";

/** Six persisted fields = six terminal classifier outcomes (QUALITY … IMPORT_FAILED). PENDING_WAITING / UNKNOWN have no field. */
export type FailedImportCleanupPolicyAxis = {
  handling_quality_rejection: FailedImportQueueHandlingAction;
  handling_unmatched_manual_import: FailedImportQueueHandlingAction;
  handling_sample_release: FailedImportQueueHandlingAction;
  handling_corrupt_import: FailedImportQueueHandlingAction;
  handling_failed_download: FailedImportQueueHandlingAction;
  handling_failed_import: FailedImportQueueHandlingAction;
  cleanup_drive_schedule_enabled: boolean;
  cleanup_drive_schedule_interval_seconds: number;
};

export type FetcherFailedImportCleanupPolicyOut = {
  movies: FailedImportCleanupPolicyAxis;
  tv_shows: FailedImportCleanupPolicyAxis;
  updated_at: string;
};

/** PUT body (CSRF added by putFailedImportCleanupPolicy). */
export type FetcherFailedImportCleanupPolicyPutBody = {
  movies: FailedImportCleanupPolicyAxis;
  tv_shows: FailedImportCleanupPolicyAxis;
};
