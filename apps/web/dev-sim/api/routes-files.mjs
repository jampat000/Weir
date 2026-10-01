/** The files Weir has picked up: the list Processing and History read, and what a person can do to one file. */
import { needsAttention, STATUS } from "../engine/file.mjs";
import { JOB_KIND } from "../engine/jobs.mjs";
import { logEntryFor } from "../engine/records.mjs";
import { shaped } from "../openapi/skeleton.mjs";
import { toWire, DAY_MS } from "../wire-time.mjs";
import { fileLogText, trackStreams } from "./file-details.mjs";
import { contains, intParam, listParam } from "./query.mjs";
import { noContent, notFound, Reply } from "./reply.mjs";
import { fileOut } from "./views.mjs";

const DEFAULT_LIST_LIMIT = 200;
const LOG_RETENTION_DAYS = 90;
const NOT_FOUND = "Weir has no file with that number.";

function matching(sim, query) {
  const libraryId = intParam(query, "library_id");
  const withinDays = intParam(query, "within_days");
  const needle = query.get("path_contains") ?? "";
  const since = withinDays === null ? null : sim.now() - withinDays * DAY_MS;
  return [...sim.engine.files.values()]
    .filter((file) => libraryId === null || file.libraryId === libraryId)
    .filter((file) => contains(file.relativePath, needle))
    .filter((file) => since === null || file.updatedAt >= since)
    .sort((a, b) => b.updatedAt - a.updatedAt || b.id - a.id);
}

function listFiles(sim, query) {
  const statuses = listParam(query, "file_status");
  const limit = intParam(query, "limit") ?? DEFAULT_LIST_LIMIT;
  const live = new Map(
    sim.engine
      .liveProgress(sim.now())
      .map((entry) => [entry.relative_path, entry]),
  );
  const candidates = matching(sim, query);
  const statusCounts = {};
  for (const file of candidates)
    statusCounts[file.status] = (statusCounts[file.status] ?? 0) + 1;
  const shown = candidates
    .filter((file) => statuses.length === 0 || statuses.includes(file.status))
    .slice(0, limit);
  return {
    files: shown.map((file) =>
      fileOut(file, live.get(file.relativePath) ?? null),
    ),
    status_counts: statusCounts,
    returned: shown.length,
    limit,
  };
}

function filesAtOnce(sim) {
  const { engine } = sim;
  const running = engine.running();
  const waiting = engine.waitingFiles().length;
  const slots = engine.slots();
  let waitingFor = "nothing";
  if (waiting > 0)
    waitingFor = engine.pause.paused
      ? "paused"
      : running >= slots
        ? "free_slot"
        : "starting";
  return shaped("ProcessingFilesAtOnceOut", {
    files_at_once: sim.store.operator.max_concurrent_files,
    worker_slots: 4,
    effective_files_at_once: slots,
    running,
    waiting,
    waiting_for: waitingFor,
    message:
      waiting === 0
        ? "Nothing is waiting for a free slot."
        : `${waiting} ${waiting === 1 ? "file is" : "files are"} waiting for a free slot.`,
    slots_note: "Weir has 4 worker slots.",
  });
}

function requeued(count, skipped = 0) {
  return {
    detail:
      count === 0
        ? "Nothing needed to be queued again."
        : `Queued ${count} ${count === 1 ? "file" : "files"} to be processed again.`,
    requeued: count,
    skipped,
  };
}

function requeueMatching(sim, body) {
  const ids = Array.isArray(body.file_ids) ? new Set(body.file_ids) : null;
  const status = body.file_status ?? null;
  const query = new URLSearchParams();
  if (body.library_id) query.set("library_id", String(body.library_id));
  if (body.path_contains) query.set("path_contains", body.path_contains);
  const chosen = matching(sim, query).filter((file) =>
    ids
      ? ids.has(file.id)
      : status
        ? file.status === status
        : needsAttention(file),
  );
  const done = chosen.filter((file) =>
    sim.engine.requeue(file.id, sim.now()),
  ).length;
  return requeued(done, chosen.length - done);
}

function rejectedFiles(sim, libraryId) {
  return [...sim.engine.files.values()].filter(
    (file) =>
      file.status === STATUS.REJECTED &&
      (libraryId === null || file.libraryId === libraryId),
  );
}

function removeFile(sim, file, body) {
  const resolution = body.resolution ?? "remove";
  sim.engine.remove(file.id);
  if (resolution === "keep") {
    sim.engine.kept.push({
      file,
      listing: {
        id: file.id,
        library_id: file.libraryId,
        library_name: file.libraryName,
        relative_path: file.relativePath,
        size_bytes: file.sizeBytes,
        kept_at: toWire(sim.now()),
      },
    });
    return {
      done: true,
      detail: "Weir stopped tracking this file and left it where it is.",
    };
  }
  if (resolution === "retry") {
    sim.engine.files.set(file.id, file);
    return {
      done: sim.engine.requeue(file.id, sim.now()),
      detail: "Weir queued the file to be processed again.",
    };
  }
  return resolution === "delete"
    ? { done: true, detail: "Weir deleted the file and stopped tracking it." }
    : noContent();
}

function fileLog(sim, file) {
  const entries =
    file.finishedAt === null
      ? []
      : [logEntryFor(file, file.id * 10, sim.now())];
  return {
    file_id: file.id,
    relative_path: file.relativePath,
    retention_days: LOG_RETENTION_DAYS,
    entries,
  };
}

function whyHeld(file) {
  const blocked = file.status === STATUS.BLOCKED_UPSTREAM;
  return shaped("ProcessingWhyHeldOut", {
    file_id: file.id,
    relative_path: file.relativePath,
    library_name: file.libraryName,
    recorded_status: file.status,
    recorded_reason: file.statusReason,
    verdict: blocked ? "wait_upstream" : "proceed",
    owned: true,
    blocked_upstream: blocked,
    blocked_by_connection: file.blockedBy,
    queue_row_count: blocked ? 1 : 0,
    managers_consulted: 1,
    managers_reporting: 1,
    reasons: blocked
      ? [`${file.blockedBy} is still importing this download.`]
      : ["Nothing is holding this file."],
  });
}

/** @param {import("./router.mjs").Router} router */
export function registerFileRoutes(router) {
  const withFile = (handler) => (context) => {
    const file = context.sim.engine.files.get(Number(context.params.id));
    return file ? handler(file, context) : notFound(NOT_FOUND);
  };
  const base = "/api/v1/processing/files";

  router.get(base, ({ sim, query }) => listFiles(sim, query));
  router.get("/api/v1/processing/files-at-once", ({ sim }) => filesAtOnce(sim));
  router.get("/api/v1/processing/reject-support", () => ({
    available: true,
    reason: "",
  }));
  router.get(`${base}/rejected/summary`, ({ sim, query }) => {
    const rejected = rejectedFiles(sim, intParam(query, "library_id")).length;
    return { ready: rejected, rejected };
  });
  router.post(`${base}/rejected/process-again`, ({ sim, body }) => {
    const chosen = rejectedFiles(sim, body.library_id ?? null);
    return requeued(
      chosen.filter((file) => sim.engine.requeue(file.id, sim.now())).length,
    );
  });
  router.post(`${base}/requeue`, ({ sim, body }) => requeueMatching(sim, body));
  router.post(
    `${base}/:id/requeue`,
    withFile((file, { sim }) => {
      const done = sim.engine.requeue(file.id, sim.now());
      return requeued(done ? 1 : 0, done ? 0 : 1);
    }),
  );
  router.post(
    `${base}/:id/move-to-top`,
    withFile((file, { sim }) => ({
      moved: sim.engine.moveToTop(file.id),
      detail: "This file will be next to start.",
    })),
  );
  router.post(
    `${base}/:id/manual-plan`,
    withFile((file, { sim }) => {
      sim.engine.requeue(file.id, sim.now());
      return {
        ok: true,
        job_id: file.jobId,
        job_kind: JOB_KIND.FILE_PASS,
        dedupe_key: `${JOB_KIND.FILE_PASS}:${file.relativePath}`,
      };
    }),
  );
  router.delete(
    `${base}/:id`,
    withFile((file, { sim, body }) => removeFile(sim, file, body)),
  );
  router.get(
    `${base}/:id/remove-options`,
    withFile(() =>
      shaped("ProcessingFileRemoveOptionsOut", {
        requires_choice: false,
        fingerprint_recorded: true,
      }),
    ),
  );
  router.get(
    `${base}/:id/log`,
    withFile((file, { sim }) => fileLog(sim, file)),
  );
  router.get(
    `${base}/:id/log/download`,
    withFile(
      (file, { sim }) =>
        new Reply(200, fileLogText(fileLog(sim, file)), {
          contentType: "text/plain; charset=utf-8",
          filename: `weir-file-${file.id}.txt`,
        }),
    ),
  );
  router.get(
    `${base}/:id/tracks`,
    withFile((file) => ({
      file_id: file.id,
      relative_path: file.relativePath,
      media_scope: file.mediaType,
      source_fingerprint: {
        device: 0,
        inode: 0,
        size_bytes: file.sizeBytes,
        modified_time_ns: 0,
      },
      streams: trackStreams(file),
    })),
  );
  router.get(
    `${base}/:id/why-held`,
    withFile((file) => whyHeld(file)),
  );
}
