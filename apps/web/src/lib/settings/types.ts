import type { RequestBody, Schema } from "../api/types";

/**
 * The server writes every key, null included. The schema lists the nullable ones as optional, so this
 * is kept by hand to say what actually arrives.
 */
export type AppSettings = {
  product_display_name: string;
  signed_in_home_notice: string | null;
  setup_wizard_state: "pending" | "skipped" | "completed" | string;
  app_timezone: string;
  log_retention_days: number;
  /** How far back Activity history goes. 0 keeps it until it is cleared. */
  activity_retention_days: number;
  configuration_backup_enabled: boolean;
  configuration_backup_interval_hours: number;
  configuration_backup_preferred_time: string;
  configuration_backup_last_run_at: string | null;
  updated_at: string;
};

export type AppSettingsPutBody = RequestBody<"SuiteSettingsPutIn">;

export type SecurityOverview = Schema<"SuiteSecurityOverviewOut">;
export type ConfigurationBackupList = Schema<"SuiteConfigurationBackupListOut">;
export type UpdateStatus = Schema<"SuiteUpdateStatusOut">;
export type UpdateSettingsOut = Schema<"UpdateSettingsOut">;
export type UpdateMode = UpdateSettingsOut["mode"];
export type UpdateSettingsPutBody = RequestBody<"UpdateSettingsPutIn">;

export type UpdateStateOut = {
  downloaded: boolean;
  pending_version: string | null;
};

export type HistoryResetResult = Schema<"SuiteOperationalHistoryResetOut">;
export type ServerLogEntry = Schema<"SuiteLogEntryOut">;
export type ServerLogs = Schema<"SuiteLogsOut">;
export type ServerMetrics = Schema<"SuiteMetricsOut">;
export type NotificationChannelOut = Schema<"NotificationChannelOut">;
export type NotificationChannelListOut = Schema<"NotificationChannelListOut">;
export type NotificationChannelIn = RequestBody<"NotificationChannelIn">;
export type ServerLogFilters = {
  level?: string;
  search?: string;
  has_exception?: boolean;
  limit?: number;
};

export type NotificationChannelTestOut = {
  ok: boolean;
  error: string | null;
};
