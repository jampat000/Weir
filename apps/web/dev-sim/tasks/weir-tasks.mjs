/**
 * The periodic tasks that belong to Weir itself rather than to one workflow: the two clean-ups, the configuration
 * backup check, and the housekeeping that runs on a timer. Their keys and labels are the server's.
 */
import { addBackup } from "../store.mjs";
import { fromWire, HOUR_MS, MINUTE_MS, SECOND_MS } from "../wire-time.mjs";
import { TimedTask } from "./timed-task.mjs";

export const TASK_KEY = Object.freeze({
  LEFTOVER_FILES: "cleanup-leftover-files",
  UNCLAIMED_COPIES: "cleanup-unclaimed-copies",
  BACKUP: "suite-configuration-backup",
  ARTWORK: "artwork-resolver",
  UPDATE: "update-check",
});

const ACTIVITY_TYPE = Object.freeze({
  LEFTOVER_FILES: "processing.work_temp_stale_sweep_completed",
  UNCLAIMED_COPIES: "processing.unclaimed_handback_cleanup_completed",
  BACKUP_DONE: "suite.configuration_backup_completed",
  BACKUP_FAILED: "suite.configuration_backup_failed",
});

const SECONDS_PER_HOUR = 3600;
const SECONDS_PER_MINUTE = 60;
const BACKUP_CHECK_SECONDS = 60;
const ARTWORK_SECONDS = 6 * SECONDS_PER_HOUR;
/** The soonest a task that is already due when the session opens runs. */
const SOONEST_RUN_MS = 20 * SECOND_MS;

const CLEANUP_RUN_MS = [2_000, 4_500];
const BACKUP_RUN_MS = [3_000, 6_000];
const QUICK_RUN_MS = [400, 1_400];
const LOOKUP_RUN_MS = [2_500, 5_500];

/**
 * The tasks that only keep house, with how often they run, how long a run lasts, and how far through its interval each
 * is when the session opens, so they do not all fall due together.
 */
const HOUSEKEEPING = [
  {
    key: "platform-job-rows-retention",
    label: "Tidy finished jobs",
    seconds: SECONDS_PER_HOUR,
    runMs: QUICK_RUN_MS,
    elapsed: 0.62,
  },
  {
    key: "processing-vanished-file-sweep",
    label: "Look for vanished files",
    seconds: 5 * SECONDS_PER_MINUTE,
    runMs: QUICK_RUN_MS,
    elapsed: 0.7,
  },
  {
    key: "media-manager-heartbeat",
    label: "Check media managers",
    seconds: SECONDS_PER_MINUTE,
    runMs: QUICK_RUN_MS,
    elapsed: 0.45,
  },
  {
    key: "processing-file-log-retention",
    label: "Prune file history",
    seconds: SECONDS_PER_HOUR,
    runMs: QUICK_RUN_MS,
    elapsed: 0.3,
  },
  {
    key: "auth-session-cleanup",
    label: "Clear old sign-ins",
    seconds: SECONDS_PER_HOUR,
    runMs: QUICK_RUN_MS,
    elapsed: 0.8,
  },
  {
    key: "suite-log-retention",
    label: "Trim the log",
    seconds: SECONDS_PER_HOUR,
    runMs: QUICK_RUN_MS,
    elapsed: 0.15,
  },
];

const succeededAt = (at) => ({ at, ok: true });

/** When a task last run at `lastAt` is next due, which is never sooner than shortly after the session opens. */
function dueAfter(lastAt, intervalSeconds, { startedAt, speed }) {
  return Math.max(
    startedAt + SOONEST_RUN_MS,
    lastAt + (intervalSeconds * SECOND_MS) / speed,
  );
}

/** A task that is `elapsed` of the way through its interval when the session opens. */
function midway(context, { seconds, runMs, elapsed }) {
  const { rng, startedAt, speed } = context;
  const intervalMs = (seconds * SECOND_MS) / speed;
  const lastAt = startedAt - intervalMs * elapsed;
  return new TimedTask({
    speed,
    intervalSeconds: () => seconds,
    runMs,
    rng,
    last: succeededAt(lastAt),
    nextRunAt: lastAt + intervalMs,
  });
}

function recordEvent(engine, nowMs, entry) {
  engine.activity.record({ trigger: "scheduled", ...entry }, nowMs);
  engine.touch();
}

/** A clean-up that writes down in the Activity record each time it finishes. */
function cleanupTask(context, { type, title, seconds, agoMs }) {
  const { engine, rng, startedAt, speed } = context;
  const last =
    engine.activity.all().findLast((event) => event.type === type)?.createdAt ??
    startedAt - agoMs;
  return new TimedTask({
    speed,
    intervalSeconds: seconds,
    runMs: CLEANUP_RUN_MS,
    rng,
    last: succeededAt(last),
    nextRunAt: dueAfter(last, seconds(), context),
    perform: (nowMs) => {
      recordEvent(engine, nowMs, { type, title, detail: { removed: 0 } });
      return { ok: true };
    },
  });
}

/**
 * The configuration backup check: it looks every minute and writes a backup when one is due. When the scenario makes it
 * fail, a backup was due and could not be written, and each try fails the same way.
 */
function backupTask(context) {
  const { engine, rng, startedAt, speed, failing } = context;
  const { suite } = engine.store;
  const failsNow = failing?.taskKey === TASK_KEY.BACKUP;
  const backupEveryMs = () =>
    (suite.configuration_backup_interval_hours * HOUR_MS) / speed;
  let lastBackupAt = Math.min(
    fromWire(suite.configuration_backup_last_run_at) ?? startedAt,
    startedAt,
  );
  const failedAt = startedAt - (failing?.failedMinutesAgo ?? 0) * MINUTE_MS;
  const retryMs = (failing?.retryMinutes ?? 0) * MINUTE_MS;
  const checkedAt = startedAt - (BACKUP_CHECK_SECONDS * SECOND_MS) / speed / 2;
  return new TimedTask({
    speed,
    intervalSeconds: () => BACKUP_CHECK_SECONDS,
    runMs: BACKUP_RUN_MS,
    rng,
    last: failsNow
      ? { at: failedAt, ok: false, error: failing.error }
      : succeededAt(checkedAt),
    nextRunAt: failsNow
      ? failedAt + retryMs / speed
      : checkedAt + (BACKUP_CHECK_SECONDS * SECOND_MS) / speed,
    retryMs: failsNow ? retryMs : undefined,
    perform: (nowMs) => {
      if (failsNow) {
        recordEvent(engine, nowMs, {
          type: ACTIVITY_TYPE.BACKUP_FAILED,
          title: "Configuration backup failed",
          result: "failed",
          detail: { reason: failing.error },
        });
        return { ok: false, error: failing.error };
      }
      if (nowMs < lastBackupAt + backupEveryMs()) return { ok: true };
      addBackup(engine.store, nowMs);
      lastBackupAt = nowMs;
      recordEvent(engine, nowMs, {
        type: ACTIVITY_TYPE.BACKUP_DONE,
        title: "Configuration backup finished",
      });
      return { ok: true };
    },
  });
}

/** A task that looks something up now and then, with nothing to show for it in the Activity record. */
function lookupTask(context, seconds) {
  const { rng, startedAt, speed } = context;
  const last = startedAt - (seconds() * SECOND_MS * 0.4) / speed;
  return new TimedTask({
    speed,
    intervalSeconds: seconds,
    runMs: LOOKUP_RUN_MS,
    rng,
    last: succeededAt(last),
    nextRunAt: dueAfter(last, seconds(), context),
  });
}

/**
 * The tasks of Weir's own wanted now, for the settings that switch them on.
 * @param {import("./definitions.mjs").TaskContext} context
 */
export function weirTasks(context) {
  const { store } = context.engine;
  const wanted = [];
  if (store.operator.work_temp_stale_sweep_enabled)
    wanted.push({
      key: TASK_KEY.LEFTOVER_FILES,
      label: "Clear leftover files",
      create: () =>
        cleanupTask(context, {
          type: ACTIVITY_TYPE.LEFTOVER_FILES,
          title: "Temporary files cleanup finished",
          seconds: () => store.operator.work_temp_stale_sweep_interval_seconds,
          agoMs: 40 * MINUTE_MS,
        }),
    });
  if (store.operator.unclaimed_handback_cleanup_enabled)
    wanted.push({
      key: TASK_KEY.UNCLAIMED_COPIES,
      label: "Clear unclaimed copies",
      create: () =>
        cleanupTask(context, {
          type: ACTIVITY_TYPE.UNCLAIMED_COPIES,
          title: "Cleanup of copies nobody picked up finished",
          seconds: () =>
            store.operator.unclaimed_handback_cleanup_interval_seconds,
          agoMs: 125 * MINUTE_MS,
        }),
    });
  wanted.push(
    ...HOUSEKEEPING.map(({ key, label, ...timing }) => ({
      key,
      label,
      create: () => midway(context, timing),
    })),
  );
  if (store.suite.configuration_backup_enabled)
    wanted.push({
      key: TASK_KEY.BACKUP,
      label: "Configuration backup check",
      create: () => backupTask(context),
    });
  if (store.metadataProvider.artwork_enabled)
    wanted.push({
      key: TASK_KEY.ARTWORK,
      label: "Look up artwork",
      create: () => lookupTask(context, () => ARTWORK_SECONDS),
    });
  wanted.push({
    key: TASK_KEY.UPDATE,
    label: "Check for updates",
    create: () =>
      lookupTask(
        context,
        () => store.updateSettings.check_interval_minutes * SECONDS_PER_MINUTE,
      ),
  });
  return wanted;
}
