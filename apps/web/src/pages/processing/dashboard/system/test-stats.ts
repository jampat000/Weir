/** A stats reading for the System cards' tests: one second of history, a machine with two cores of work, one drive. */
import type {
  SystemNow,
  SystemOverview,
  SystemStats,
} from "../../../../lib/system/system-stats-types";

const MB = 1024 * 1024;
const GB = 1024 * MB;

export const testNow: SystemNow = {
  at: "2026-10-02T12:00:01Z",
  cpu_percent: 37.4,
  cores: 16,
  memory_used_bytes: 11 * GB,
  memory_total_bytes: 32 * GB,
  disk_read_bytes_per_sec: 4 * MB,
  disk_write_bytes_per_sec: 12 * MB,
  disk_busy_percent: 18,
  weir_cpu_percent: 3,
  weir_memory_bytes: 255 * MB,
  tools_cpu_percent: 41,
  processing_read_bytes_per_sec: 20 * MB,
  processing_write_bytes_per_sec: 6 * MB,
  processing_speed: 148,
  running: 2,
  slots: 4,
};

export const testStats: SystemStats = {
  interval_ms: 1000,
  window_s: 600,
  now: testNow,
  history: [
    {
      at: "2026-10-02T12:00:00Z",
      cpu_percent: 30,
      memory_percent: 34,
      disk_read_bytes_per_sec: 4 * MB,
      disk_write_bytes_per_sec: 10 * MB,
      processing_read_bytes_per_sec: 18 * MB,
      processing_write_bytes_per_sec: 5 * MB,
      processing_speed: 140,
    },
    {
      at: "2026-10-02T12:00:01Z",
      cpu_percent: 37,
      memory_percent: 34,
      disk_read_bytes_per_sec: 4 * MB,
      disk_write_bytes_per_sec: 12 * MB,
      processing_read_bytes_per_sec: 20 * MB,
      processing_write_bytes_per_sec: 6 * MB,
      processing_speed: 148,
    },
  ],
  machine: {
    os: "Windows 11 Pro",
    uptime_seconds: 2 * 86_400,
    reboot_pending: true,
  },
  drives: [
    {
      name: "D:",
      path: "D:\\",
      total_bytes: 1000 * GB,
      free_bytes: 400 * GB,
      weir_bytes: 100 * GB,
      keep_free_bytes: 20 * GB,
      full_in_days: 12,
      read_bytes_per_sec: 4 * MB,
      write_bytes_per_sec: 12 * MB,
      busy_percent: 18,
      workflows: [{ id: 1, name: "Movies", roles: ["output"] }],
    },
  ],
};

export const testOverview: SystemOverview = {
  version: "3.2.16",
  update: { status: "up_to_date", latest_version: "3.2.16" },
  uptime_seconds: 3600,
  started_at: "2026-10-02T11:00:00Z",
  runs_as: "service",
  address: "http://192.168.1.5:9347/",
  data_bytes: 5 * MB,
  browsers_live: 2,
  requests: { median_ms: 6.4, p95_ms: 20, errors_today: 0 },
  jobs_today: { run: 1200, failed: 2 },
  restarts_this_week: 0,
  checks: { passing: 9, total: 10 },
  last_update_backup: null,
};
