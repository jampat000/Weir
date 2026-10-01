/** The work queue and what came of it: jobs, kept files, library cleans, the totals, and asking Weir to look again. */
import { JOB_KIND, jobRow } from "../engine/jobs.mjs";
import { shaped } from "../openapi/skeleton.mjs";
import { DAY_MS } from "../wire-time.mjs";
import { contains, intParam, listParam } from "./query.mjs";
import { notFound } from "./reply.mjs";
import { overviewStats } from "./stats.mjs";

const DEFAULT_JOB_LIMIT = 100;
const DEFAULT_CLEAN_LIMIT = 200;

function inspectJobs(sim, query) {
  const statuses = listParam(query, "status");
  const limit = intParam(query, "limit") ?? DEFAULT_JOB_LIMIT;
  const knownFilesOnly = query.get("known_files_only") === "true";
  const known = new Set(
    [...sim.engine.files.values()].map((file) => file.relativePath),
  );
  const jobs = sim.engine.jobs
    .all()
    .filter((job) => statuses.length === 0 || statuses.includes(job.status))
    .filter(
      (job) =>
        !knownFilesOnly ||
        job.kind !== JOB_KIND.FILE_PASS ||
        known.has(String(job.payload.relative_media_path)),
    )
    .slice(0, limit);
  return {
    default_recent_slice: statuses.length === 0,
    jobs: jobs.map(jobRow),
  };
}

function libraryCleans(sim, query) {
  const libraryId = intParam(query, "library_id");
  const withinDays = intParam(query, "within_days");
  const needle = query.get("path_contains") ?? "";
  const limit = intParam(query, "limit") ?? DEFAULT_CLEAN_LIMIT;
  const since = withinDays === null ? null : sim.now() - withinDays * DAY_MS;
  const cleans = sim.engine.cleanRecords
    .filter((clean) => libraryId === null || clean.library_id === libraryId)
    .filter((clean) => contains(clean.relative_path, needle))
    .filter((clean) => since === null || Date.parse(clean.recorded_at) >= since)
    .slice(0, limit);
  return { cleans, returned: cleans.length, limit };
}

function enqueued(job) {
  return {
    ok: true,
    job_id: job?.id ?? 0,
    job_kind: job?.kind ?? JOB_KIND.FILE_PASS,
    dedupe_key: `${job?.kind ?? JOB_KIND.FILE_PASS}:${job?.id ?? 0}`,
  };
}

/** Asking Weir to look at a workflow's folder again: a new download turns up, as it would after a scan. */
function lookAgain(sim, body) {
  const library =
    sim.store.libraries.find((candidate) => candidate.id === body.library_id) ??
    sim.store.libraries.find((candidate) => candidate.enabled);
  const file = library ? sim.engine.admit(library, sim.now()) : null;
  return enqueued(file ? sim.engine.jobs.get(file.jobId) : null);
}

/** @param {import("./router.mjs").Router} router */
export function registerWorkRoutes(router) {
  router.get("/api/v1/processing/jobs/inspection", ({ sim, query }) =>
    inspectJobs(sim, query),
  );
  router.post(
    "/api/v1/processing/jobs/:id/cancel-pending",
    ({ sim, params }) => {
      const job = sim.engine.jobs.get(Number(params.id));
      if (!job) return notFound("Weir has no job with that number.");
      sim.engine.cancelJob(job.id, sim.now());
      return { ok: true, job_id: job.id, status: job.status };
    },
  );
  router.post(
    "/api/v1/processing/jobs/file-remux-pass/enqueue",
    ({ sim, body }) => {
      const file = [...sim.engine.files.values()].find(
        (candidate) => candidate.relativePath === body.relative_media_path,
      );
      if (file) sim.engine.requeue(file.id, sim.now());
      return enqueued(file ? sim.engine.jobs.get(file.jobId) : null);
    },
  );
  router.post(
    "/api/v1/processing/jobs/watched-folder-remux-scan-dispatch/enqueue",
    ({ sim, body }) => lookAgain(sim, body),
  );
  router.get("/api/v1/processing/overview-stats", ({ sim, query }) =>
    overviewStats(sim.engine, sim.now(), intParam(query, "window_days")),
  );
  router.get("/api/v1/processing/library-cleans", ({ sim, query }) =>
    libraryCleans(sim, query),
  );
  router.get("/api/v1/processing/kept-files", ({ sim }) => ({
    files: sim.engine.kept.map(({ listing }) =>
      shaped("ProcessingKeptFileOut", listing),
    ),
  }));
  router.post(
    "/api/v1/processing/kept-files/:id/process-again",
    ({ sim, params }) => {
      const index = sim.engine.kept.findIndex(
        (kept) => kept.listing.id === Number(params.id),
      );
      if (index < 0)
        return notFound("Weir is not keeping a file with that number.");
      const [{ file }] = sim.engine.kept.splice(index, 1);
      sim.engine.files.set(file.id, file);
      sim.engine.requeue(file.id, sim.now());
      return { detail: "Weir will process this file again." };
    },
  );
}
