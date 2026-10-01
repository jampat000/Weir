/** System › Logs: one line for each recent Activity entry, worded the way a service log reads. */
import { toWire } from "../wire-time.mjs";

const DEFAULT_LIMIT = 200;
const LEVEL_BY_RESULT = {
  failed: "Error",
  warning: "Warning",
  retrying: "Warning",
};

function lineFor(event) {
  const level = LEVEL_BY_RESULT[event.result] ?? "Information";
  const subject = event.relativePath
    ? ` ${event.relativePath.split(/[\\/]/).pop()}`
    : "";
  return {
    timestamp: toWire(event.createdAt),
    level,
    logger: `weir.${event.type.split(".")[0]}`,
    component: event.type.split(".")[0],
    message: `${event.title}${subject}`,
    detail: null,
    traceback: null,
    correlation_id: null,
    job_id: null,
    source: null,
  };
}

/**
 * @param {import("../sim.mjs").Sim} sim
 * @param {URLSearchParams} query
 */
export function activityLogLines(sim, query) {
  const level = query.get("level")?.toLowerCase();
  const search = query.get("search")?.toLowerCase();
  const limit = Number(query.get("limit")) || DEFAULT_LIMIT;
  const all = sim.engine.activity.all().map(lineFor).reverse();
  const counts = { information: 0, warning: 0, error: 0 };
  for (const line of all) counts[line.level.toLowerCase()] += 1;
  const matching = all.filter(
    (line) =>
      (!level || line.level.toLowerCase() === level) &&
      (!search || line.message.toLowerCase().includes(search)),
  );
  return { counts, items: matching.slice(0, limit), total: matching.length };
}
