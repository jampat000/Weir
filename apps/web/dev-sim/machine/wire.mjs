/** The machine's readings as the API words them: `GET /api/v1/system/stats` and the `system.stats` frame. */
import { toWire, SECOND_MS } from "../wire-time.mjs";
import { HARDWARE } from "./profiles.mjs";

const PERCENT = 100;
const TENTH = 10;
const RING_SECONDS = 600;

/** @param {number} value */
const oneDecimal = (value) => Math.round(value * TENTH) / TENTH;

/** One second's reading, as an entry of the history. @param {import("./model.mjs").Sample} sample */
export function pointOut(sample) {
  return {
    at: toWire(sample.atMs),
    cpu_percent: oneDecimal(sample.cpuPercent),
    memory_percent: oneDecimal(
      (sample.memoryUsedBytes / sample.memoryTotalBytes) * PERCENT,
    ),
    disk_read_bytes_per_sec: sample.diskReadBytesPerSecond,
    disk_write_bytes_per_sec: sample.diskWriteBytesPerSecond,
    processing_read_bytes_per_sec: sample.processingReadBytesPerSecond,
    processing_write_bytes_per_sec: sample.processingWriteBytesPerSecond,
    processing_speed: oneDecimal(sample.processingSpeed),
  };
}

/** The newest reading in full. @param {import("./model.mjs").Sample} sample */
export function nowOut(sample) {
  return {
    at: toWire(sample.atMs),
    cpu_percent: oneDecimal(sample.cpuPercent),
    cores: sample.cores,
    memory_used_bytes: sample.memoryUsedBytes,
    memory_total_bytes: sample.memoryTotalBytes,
    disk_read_bytes_per_sec: sample.diskReadBytesPerSecond,
    disk_write_bytes_per_sec: sample.diskWriteBytesPerSecond,
    disk_busy_percent: oneDecimal(sample.diskBusyPercent),
    weir_cpu_percent: oneDecimal(sample.weirCpuPercent),
    weir_memory_bytes: sample.weirMemoryBytes,
    tools_cpu_percent: oneDecimal(sample.toolsCpuPercent),
    processing_read_bytes_per_sec: sample.processingReadBytesPerSecond,
    processing_write_bytes_per_sec: sample.processingWriteBytesPerSecond,
    processing_speed: oneDecimal(sample.processingSpeed),
    running: sample.running,
    slots: sample.slots,
  };
}

/** The `system.stats` frame for a reading. @param {import("./model.mjs").Sample} sample */
export const statsFrame = (sample) => ({
  now: nowOut(sample),
  point: pointOut(sample),
});

/** @param {import("./drives.mjs").DriveReading} drive */
function driveOut(drive) {
  return {
    name: drive.name,
    path: drive.path,
    total_bytes: drive.totalBytes,
    free_bytes: Math.round(drive.freeBytes),
    weir_bytes: Math.round(drive.weirBytes),
    keep_free_bytes: drive.keepFreeBytes,
    full_in_days: drive.fullInDays,
    read_bytes_per_sec:
      drive.readBytesPerSecond === null
        ? null
        : Math.round(drive.readBytesPerSecond),
    write_bytes_per_sec:
      drive.writeBytesPerSecond === null
        ? null
        : Math.round(drive.writeBytesPerSecond),
    busy_percent:
      drive.busyPercent === null ? null : oneDecimal(drive.busyPercent),
    workflows: drive.workflows,
  };
}

/**
 * The whole of `GET /api/v1/system/stats`.
 * @param {import("./machine.mjs").Machine} machine
 * @param {number} nowMs
 */
export function statsOut(machine, nowMs) {
  const newest = machine.latest;
  return {
    interval_ms: SECOND_MS,
    window_s: RING_SECONDS,
    now: nowOut(newest),
    history: machine.history().map(pointOut),
    machine: {
      os: HARDWARE.os,
      uptime_seconds: Math.round(machine.uptimeMs(nowMs) / SECOND_MS),
      reboot_pending: machine.profile.rebootPending,
    },
    drives: machine.drives(nowMs).map(driveOut),
  };
}
