/** The totals Processing shows for a window of days, counted from the files Weir has finished. */
import { shaped } from "../openapi/skeleton.mjs";
import { STATUS } from "../engine/file.mjs";
import { VERDICT } from "../engine/plan.mjs";
import { DAY_MS } from "../wire-time.mjs";

const PERCENT = 100;
const ROUNDING = 10;
const DEFAULT_WINDOW_DAYS = 30;

/** @param {number} value */
const oneDecimal = (value) => Math.round(value * ROUNDING) / ROUNDING;

/**
 * @param {import("../engine/engine.mjs").Engine} engine
 * @param {number} nowMs
 * @param {number | null} windowDays
 */
export function overviewStats(engine, nowMs, windowDays) {
  const days = windowDays ?? DEFAULT_WINDOW_DAYS;
  const since = nowMs - days * DAY_MS;
  const ended = [...engine.files.values()].filter(
    (file) => file.finishedAt !== null && file.finishedAt >= since,
  );
  const finished = ended.filter((file) => file.status === STATUS.PROCESSED);
  const written = finished.filter(
    (file) => file.plan.verdict === VERDICT.CLEAN,
  );
  const failed = ended.filter((file) => file.status === STATUS.FAILED);
  const sourceBytes = written.reduce((sum, file) => sum + file.sizeBytes, 0);
  const savedBytes = written.reduce(
    (sum, file) =>
      sum + file.sizeBytes - (file.plan.outputBytes ?? file.sizeBytes),
    0,
  );
  const attempted = finished.length + failed.length;
  return shaped("ProcessingOverviewStatsOut", {
    window_days: days,
    files_processed: finished.length,
    files_failed: failed.length,
    output_written_count: written.length,
    already_optimized_count: finished.length - written.length,
    net_space_saved_bytes: savedBytes,
    net_space_saved_percent:
      sourceBytes === 0 ? 0 : oneDecimal((savedBytes / sourceBytes) * PERCENT),
    success_rate_percent:
      attempted === 0
        ? PERCENT
        : oneDecimal((finished.length / attempted) * PERCENT),
  });
}
