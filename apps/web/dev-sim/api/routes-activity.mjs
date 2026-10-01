/** The Activity record: the feed Processing and System › Logs read, a file's history, and the export. */
import { EVENT_TYPE } from "../engine/records.mjs";
import { shaped } from "../openapi/skeleton.mjs";
import { toWire } from "../wire-time.mjs";
import { contains, intParam } from "./query.mjs";
import { download } from "./reply.mjs";
import { activityItem } from "./views.mjs";

const DEFAULT_PAGE = 100;
const FILES_ABOUT = "files";
const WEIR_ABOUT = "weir";

/** Whether an entry about a file is still worth showing: the file has not been removed from History. */
function aboutKnownFile(sim, event) {
  if (
    event.relativePath === null ||
    event.type === EVENT_TYPE.LIBRARY_FILE_CLEANED
  )
    return true;
  return [...sim.engine.files.values()].some(
    (file) =>
      file.relativePath === event.relativePath &&
      file.libraryId === event.libraryId,
  );
}

const moduleMatches = (module, event) =>
  event.type.startsWith(`${module}.`) ||
  (module === "processing" && event.type.startsWith("library."));

function aboutMatches(about, event) {
  if (about === FILES_ABOUT) return event.relativePath !== null;
  return about === WEIR_ABOUT ? event.relativePath === null : true;
}

function matchesFilters(sim, query, event) {
  const text = (name) => query.get(name) ?? "";
  const libraryId = intParam(query, "library_id");
  const from = query.get("date_from");
  const to = query.get("date_to");
  const haystack = `${event.title} ${event.relativePath ?? ""} ${JSON.stringify(event.detail)}`;
  return [
    !text("module") || moduleMatches(text("module"), event),
    !text("event_type") || event.type === text("event_type"),
    !text("trigger") || event.trigger === text("trigger"),
    !text("result") || event.result === text("result"),
    libraryId === null || event.libraryId === libraryId,
    contains(haystack, text("search")),
    contains(event.relativePath ?? "", text("file")),
    aboutMatches(query.get("about"), event),
    !from || event.createdAt >= Date.parse(from),
    !to || event.createdAt <= Date.parse(to),
    text("known_files_only") !== "true" || aboutKnownFile(sim, event),
  ].every(Boolean);
}

/** Every entry the filters keep, newest first. */
function filtered(sim, query) {
  return sim.engine.activity
    .all()
    .filter((event) => matchesFilters(sim, query, event))
    .reverse();
}

function recent(sim, query) {
  const all = filtered(sim, query);
  const beforeId = intParam(query, "before_id");
  const older =
    beforeId === null ? all : all.filter((event) => event.id < beforeId);
  const items = older.slice(0, intParam(query, "limit") ?? DEFAULT_PAGE);
  return shaped("ActivityRecentOut", {
    items: items.map(activityItem),
    total: all.length,
    has_more: older.length > items.length,
    oldest_event_at: sim.engine.activity.all()[0]
      ? toWire(sim.engine.activity.all()[0].createdAt)
      : null,
    retention_days: sim.store.suite.activity_retention_days,
  });
}

function csvLine(item) {
  return [
    item.created_at,
    item.event_type,
    item.result,
    item.relative_path ?? "",
    item.title,
  ]
    .map((cell) => `"${String(cell).replaceAll('"', '""')}"`)
    .join(",");
}

function exported(sim, query) {
  const items = filtered(sim, query).map(activityItem);
  if (query.get("format") === "json")
    return download(
      JSON.stringify(items, null, 2),
      "weir-activity.json",
      "application/json",
    );
  return download(
    ["time,event,result,file,title", ...items.map(csvLine)].join("\n"),
    "weir-activity.csv",
    "text/csv; charset=utf-8",
  );
}

function historyOf(sim, relativePath, libraryId) {
  const files = [...sim.engine.files.values()].filter(
    (file) =>
      file.relativePath === relativePath &&
      (libraryId === null || file.libraryId === libraryId),
  );
  const events = sim.engine.activity
    .all()
    .filter(
      (event) =>
        event.relativePath === relativePath &&
        (libraryId === null || event.libraryId === libraryId),
    );
  return { files, events };
}

/** @param {import("./router.mjs").Router} router */
export function registerActivityRoutes(router) {
  router.get("/api/v1/activity/recent", ({ sim, query }) => recent(sim, query));
  router.get("/api/v1/activity/export", ({ sim, query }) =>
    exported(sim, query),
  );
  router.get("/api/v1/activity/file-history", ({ sim, query }) => {
    const relativePath = query.get("relative_path") ?? "";
    const { files, events } = historyOf(
      sim,
      relativePath,
      intParam(query, "library_id"),
    );
    return {
      relative_path: relativePath,
      activity_events: events.length,
      processing_records: files.length,
      message: "Removing this file clears its history.",
    };
  });
  router.post("/api/v1/activity/file-history/remove", ({ sim, body }) => {
    const { files, events } = historyOf(
      sim,
      body.relative_path,
      body.library_id ?? null,
    );
    for (const file of files) sim.engine.remove(file.id);
    const gone = new Set(events);
    sim.engine.activity.retain((event) => !gone.has(event));
    return {
      relative_path: body.relative_path,
      activity_events_deleted: events.length,
      processing_records_deleted: files.length,
    };
  });
}
