/** `GET /api/v1/system/overview`: the facts about this Weir that the System screen's first card shows. */
import {
  APP_VERSION,
  updateStatus,
  WEIR_UPTIME_AT_START_MS,
} from "../fixtures/settings.mjs";
import { MACHINE_NAME } from "../fixtures/connections.mjs";
import { JOB_STATUS } from "../engine/jobs.mjs";
import { TASK_KEY } from "../tasks/weir-tasks.mjs";
import { toWire, SECOND_MS } from "../wire-time.mjs";

const PORT = 9347;
/** What the data folder weighs before anything is written to the record, and what each entry adds. */
const DATA_BASE_BYTES = 412 * 1024 ** 2;
const DATA_BYTES_PER_ENTRY = 2048;
/** How long a request takes, in ms: a little more the busier the processor is. */
const MEDIAN_MS_IDLE = 4.8;
const MEDIAN_MS_PER_CPU_PERCENT = 0.04;
const P95_OVER_MEDIAN = 3.2;
const TENTH = 10;
/** The two tools Weir needs. */
const TOOL_CHECKS = 2;
const BACKUP_CHECKS = 1;

const oneDecimal = (value) => Math.round(value * TENTH) / TENTH;

function startOfLocalDay(nowMs) {
  const day = new Date(nowMs);
  day.setHours(0, 0, 0, 0);
  return day.getTime();
}

/** Jobs finished since local midnight, and how many of them failed. */
function jobsToday(engine, nowMs) {
  const since = startOfLocalDay(nowMs);
  const finished = engine.jobs
    .all()
    .filter(
      (job) =>
        job.updatedAt >= since &&
        [JOB_STATUS.COMPLETED, JOB_STATUS.FAILED].includes(job.status),
    );
  return {
    run: finished.length,
    failed: finished.filter((job) => job.status === JOB_STATUS.FAILED).length,
  };
}

/** How many of Weir's checks pass: each workflow, each connection and each drive, the two tools and the backup. */
function checks(sim, nowMs) {
  const { store } = sim.engine;
  const workflows = store.libraries.filter((library) => library.enabled);
  const connections = [...store.managers, ...store.downloadClients];
  const drives = sim.machine.drives(nowMs);
  const backup = sim.tasks.rows().find((row) => row.key === TASK_KEY.BACKUP);
  const failing =
    connections.filter((connection) => connection.last_test_ok === false)
      .length +
    drives.filter((drive) => drive.freeBytes < drive.keepFreeBytes).length +
    (backup?.last_ok === false ? 1 : 0);
  const total =
    workflows.length +
    connections.length +
    drives.length +
    TOOL_CHECKS +
    BACKUP_CHECKS;
  return { passing: total - failing, total };
}

/** @param {import("../sim.mjs").Sim} sim */
export function overviewOut(sim) {
  const nowMs = sim.now();
  const { engine, machine } = sim;
  const update = updateStatus();
  const uptimeMs = WEIR_UPTIME_AT_START_MS + (nowMs - sim.startedAt);
  const cpu = machine.latest?.cpuPercent ?? 0;
  const median = MEDIAN_MS_IDLE + cpu * MEDIAN_MS_PER_CPU_PERCENT;
  return {
    version: APP_VERSION,
    update: { status: update.status, latest_version: update.latest_version },
    uptime_seconds: Math.round(uptimeMs / SECOND_MS),
    started_at: toWire(nowMs - uptimeMs),
    runs_as: "service",
    address: `http://${MACHINE_NAME}:${PORT}`,
    data_bytes:
      DATA_BASE_BYTES + engine.activity.all().length * DATA_BYTES_PER_ENTRY,
    browsers_live: machine.browsers,
    requests: {
      median_ms: oneDecimal(median),
      p95_ms: oneDecimal(median * P95_OVER_MEDIAN),
      errors_today: machine.profile.errorsToday,
    },
    jobs_today: jobsToday(engine, nowMs),
    restarts_this_week: machine.profile.restartsThisWeek,
    checks: checks(sim, nowMs),
  };
}
