/**
 * GET /system/log: the events, jobs and server lines of the simulation as one list (newest first unless sorted another way), with a cursor and the
 * counts each filter chip shows, as the server answers it. Only the routes and the paging are here; the rows are in
 * system-log-rows.mjs and the rules that sort them into levels and categories in system-log-rules.mjs.
 */
import { shaped } from "../openapi/skeleton.mjs";
import { toWire } from "../wire-time.mjs";
import { pageOf } from "./keyset.mjs";
import { contains, intParam, listParam } from "./query.mjs";
import { download } from "./reply.mjs";
import { readLogOrder } from "./system-log-order.mjs";
import { allRows, rowOut } from "./system-log-rows.mjs";
import { CATEGORIES, LEVELS, SOURCE } from "./system-log-rules.mjs";
import { refusal } from "./validation.mjs";

const DEFAULT_LIMIT = 50;
const MAX_LIMIT = 100;
const EXPORT_MAX_ROWS = 50_000;
const SOURCES = Object.values(SOURCE);

/** The filters of a request. @param {URLSearchParams} query */
function filtersOf(query) {
  const hasException = query.get("has_exception");
  const date = (name) => {
    const ms = Date.parse(query.get(name) ?? "");
    return Number.isNaN(ms) ? null : ms;
  };
  return {
    sources: new Set(listParam(query, "source")),
    levels: new Set(listParam(query, "level")),
    categories: new Set(listParam(query, "category")),
    statuses: new Set(listParam(query, "status")),
    workflow: intParam(query, "workflow"),
    job: intParam(query, "job"),
    text: (query.get("q") ?? "").trim(),
    eventType: query.get("event_type") ?? "",
    result: query.get("result") ?? "",
    trigger: query.get("trigger") ?? "",
    hasException: hasException === null ? null : hasException === "true",
    from: date("from"),
    to: date("to"),
  };
}

const setHas = (set, value) => set.size === 0 || set.has(value);

/** Whether a source can hold a row these filters let through: a filter only some sources have leaves the others out. */
function canMatch(filter, source) {
  const eventOnly = filter.eventType || filter.result || filter.trigger;
  if (source === SOURCE.EVENT)
    return filter.statuses.size === 0 && filter.hasException === null;
  if (source === SOURCE.JOB) return !eventOnly && filter.hasException === null;
  return filter.workflow === null && !eventOnly && filter.statuses.size === 0;
}

/** Everything but the source, the level and the category. */
function passesOther(row, filter) {
  const { facts } = row;
  return [
    filter.workflow === null || row.workflowId === filter.workflow,
    filter.job === null ||
      row.jobId === filter.job ||
      (row.source === SOURCE.JOB && row.key === filter.job),
    contains(row.text, filter.text),
    filter.from === null || row.atMs >= filter.from,
    filter.to === null || row.atMs <= filter.to,
    !filter.eventType || facts.eventType === filter.eventType,
    !filter.result || facts.result === filter.result,
    !filter.trigger || facts.trigger === filter.trigger,
    filter.statuses.size === 0 || filter.statuses.has(facts.status),
    filter.hasException === null || facts.hasException === filter.hasException,
  ].every(Boolean);
}

const tally = (keys, rows, keyOf) =>
  Object.fromEntries(
    keys.map((key) => [key, rows.filter((row) => keyOf(row) === key).length]),
  );

/** @param {import("../sim.mjs").Sim} sim @param {URLSearchParams} query @param {ReturnType<typeof readLogOrder>} order @param {number} limit */
function page(sim, query, order, limit) {
  const filter = filtersOf(query);
  const rows = allRows(sim).filter(
    (row) => canMatch(filter, row.source) && passesOther(row, filter),
  );
  const levelOk = (row) => setHas(filter.levels, row.level);
  const categoryOk = (row) => setHas(filter.categories, row.category);
  const selected = (row) => setHas(filter.sources, row.source);

  const matching = rows.filter((row) => levelOk(row) && categoryOk(row));
  const shown = matching.filter(selected);
  const { rows: items, nextCursor } = pageOf(shown, { ...order, limit });

  const counted = rows.filter(selected);
  return {
    items,
    next: nextCursor,
    total: shown.length,
    counts: {
      source: tally(SOURCES, matching, (row) => row.source),
      level: tally(LEVELS, counted.filter(categoryOk), (row) => row.level),
      category: tally(
        CATEGORIES,
        counted.filter(levelOk),
        (row) => row.category,
      ),
    },
  };
}

const workflowNames = (sim) =>
  new Map(sim.store.libraries.map((library) => [library.id, library.name]));

function systemLog(sim, query) {
  const limit = Math.min(
    MAX_LIMIT,
    Math.max(1, intParam(query, "limit") ?? DEFAULT_LIMIT),
  );
  const names = workflowNames(sim);
  const issues = [];
  const order = readLogOrder(query, names, issues);
  if (issues.length > 0) return refusal(issues);
  const result = page(sim, query, order, limit);
  return shaped("SystemLogOut", {
    items: result.items.map((row) => rowOut(row, names)),
    next_cursor: result.next,
    total: result.total,
    counts: result.counts,
  });
}

const csvCell = (cell) => `"${String(cell ?? "").replaceAll('"', '""')}"`;

function exported(sim, query) {
  const names = workflowNames(sim);
  const issues = [];
  const order = readLogOrder(query, names, issues);
  if (issues.length > 0) return refusal(issues);
  const rows = page(
    sim,
    query,
    { ...order, after: null },
    EXPORT_MAX_ROWS,
  ).items;
  if (query.get("format") === "json") {
    return download(
      JSON.stringify(
        rows.map((row) => rowOut(row, names)),
        null,
        2,
      ),
      "weir-log.json",
      "application/json",
    );
  }
  const lines = rows.map((row) =>
    [
      toWire(row.atMs),
      row.source,
      row.level,
      row.category,
      names.get(row.workflowId) ?? "",
      row.wire.title,
      row.wire.detail ?? "",
    ]
      .map(csvCell)
      .join(","),
  );
  return download(
    ["time,source,level,category,workflow,title,detail", ...lines].join("\n"),
    "weir-log.csv",
    "text/csv; charset=utf-8",
  );
}

/** @param {import("./router.mjs").Router} router */
export function registerSystemLogRoutes(router) {
  router.get("/api/v1/system/log", ({ sim, query }) => systemLog(sim, query));
  router.get("/api/v1/system/log/export", ({ sim, query }) =>
    exported(sim, query),
  );
}
