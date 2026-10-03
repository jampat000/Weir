/** The Library page: what is already in each workflow's library, and cleaning it in place. */
import { shaped } from "../openapi/skeleton.mjs";
import {
  libraryFilesPage,
  libraryMode,
  libraryOverview,
  scanState,
} from "./library-views.mjs";
import { notFound } from "./reply.mjs";
import { rulesPreview } from "./rules-preview.mjs";
import { knownFields } from "../openapi/fields.mjs";

const SCAN_JOB_ID = 1;

function cleanRequest(sim, library, body) {
  const queued = sim.engine.queueLibraryCleans(
    library.id,
    body.paths ?? [],
    sim.now(),
  );
  const chosen = sim.libraryFiles
    .list(library.id)
    .filter((file) => (body.paths ?? []).includes(file.path));
  return shaped("LibraryCleanOut", {
    queued,
    job_ids: [],
    files_count: queued,
    tracks_count: chosen.reduce(
      (sum, file) =>
        sum + file.removed_audio_tracks + file.removed_subtitle_tracks,
      0,
    ),
    estimated_bytes_saved: chosen.reduce(
      (sum, file) => sum + file.estimated_bytes_saved,
      0,
    ),
    skipped_paths:
      chosen.length < (body.paths ?? []).length
        ? (body.paths ?? []).filter(
            (path) => !chosen.some((file) => file.path === path),
          )
        : [],
    warnings: [],
  });
}

/** @param {import("./router.mjs").Router} router */
export function registerLibraryPageRoutes(router) {
  const base = "/api/v1/processing/libraries/:id";
  const withLibrary = (handler) => (context) => {
    const library = context.sim.store.libraries.find(
      (candidate) => candidate.id === Number(context.params.id),
    );
    return library
      ? handler(library, context)
      : notFound("That workflow does not exist.");
  };

  router.get(
    `${base}/library-overview`,
    withLibrary((library, { sim }) => libraryOverview(sim, library)),
  );
  router.get(
    `${base}/library-files`,
    withLibrary((library, { sim, query }) =>
      libraryFilesPage(sim, library, query),
    ),
  );
  router.get(
    `${base}/library-problems`,
    withLibrary((library, { sim }) => ({
      library_id: library.id,
      scan: scanState(sim),
      groups: [],
      total: 0,
    })),
  );
  router.get(
    `${base}/library-redownloads`,
    withLibrary((library) => ({
      library_id: library.id,
      titles: [],
      total: 0,
    })),
  );
  router.post(
    `${base}/preview`,
    withLibrary((library, { sim, body }) => rulesPreview(sim, library, body)),
  );
  router.post(
    `${base}/library-scan`,
    withLibrary(() => ({
      already_running: false,
      job_id: SCAN_JOB_ID,
      status: "completed",
    })),
  );
  router.post(
    `${base}/library-files/clean`,
    withLibrary((library, { sim, body }) => cleanRequest(sim, library, body)),
  );
  router.post(
    `${base}/library-files/leave-alone`,
    withLibrary((library, { sim, body }) => {
      const leaveAlone = body.leave_alone ?? true;
      sim.libraryFiles.setLeaveAlone(library.id, [body.path], leaveAlone);
      return { path: body.path, leave_alone: leaveAlone };
    }),
  );
  router.get(
    `${base}/library-settings`,
    withLibrary((library, { sim }) => libraryMode(sim, library)),
  );
  const saveMode = withLibrary((library, { sim, body }) =>
    Object.assign(
      libraryMode(sim, library),
      knownFields("LibrarySettingsOut", body),
    ),
  );
  router.put(`${base}/library-settings`, saveMode);
  router.post(`${base}/library-schedule`, saveMode);
}
