/** The Library page's numbers: what a scan found in each workflow's library, summed and broken down. */
import { totalsOf } from "../engine/library-files.mjs";
import { shaped } from "../openapi/skeleton.mjs";
import { toWire, HOUR_MS } from "../wire-time.mjs";

const SCAN_AGE_SECONDS = 1500;
const NEXT_RUN_HOURS = 6;
const DEFAULT_PAGE_SIZE = 100;

const SORTERS = {
  path: (file) => file.path,
  title: (file) => file.manager_title ?? "",
  size: (file) => file.size_bytes,
  saved: (file) => file.estimated_bytes_saved,
  modified: (file) => file.modified_at,
  state: (file) => file.classification,
  video: (file) => file.video_codec,
  resolution: (file) => file.video_height ?? 0,
  audio: (file) => file.audio_track_count,
  subtitles: (file) => file.subtitle_track_count,
};

export function scanState(sim) {
  return {
    job_id: 1,
    status: "completed",
    running: false,
    generated_at: Math.floor(sim.now() / 1000) - SCAN_AGE_SECONDS,
    errors: [],
  };
}

function breakdown(files, valueOf) {
  const groups = new Map();
  for (const file of files) {
    const value = valueOf(file);
    const group = groups.get(value) ?? { value, files: 0, size_bytes: 0 };
    group.files += 1;
    group.size_bytes += file.size_bytes;
    groups.set(value, group);
  }
  return [...groups.values()]
    .sort((a, b) => b.files - a.files)
    .map((group) => ({
      ...group,
      share: files.length === 0 ? 0 : group.files / files.length,
    }));
}

export function libraryOverview(sim, library) {
  const files = sim.libraryFiles.list(library.id);
  const wouldChange = files.filter(
    (file) => file.classification === "would_change",
  );
  return shaped("LibraryOverviewOut", {
    library_id: library.id,
    folders_configured: files.length > 0 ? 1 : 0,
    scan: files.length > 0 ? scanState(sim) : null,
    schedule: {
      enabled: true,
      next_run_at: toWire(sim.now() + NEXT_RUN_HOURS * HOUR_MS),
    },
    totals: totalsOf(files),
    breakdowns: {
      video_codec: breakdown(files, (file) => file.video_codec.toUpperCase()),
      resolution: breakdown(files, (file) => file.resolution_class),
      audio: breakdown(files, () => "AC-3 5.1"),
      audio_language: [
        {
          value: "English",
          files: files.length,
          share: 1,
          size_bytes: files.reduce((sum, file) => sum + file.size_bytes, 0),
        },
        ...breakdown(wouldChange, () => "French"),
      ],
      subtitle_language: [
        {
          value: "English",
          files: files.length,
          share: 1,
          size_bytes: files.reduce((sum, file) => sum + file.size_bytes, 0),
        },
        ...breakdown(wouldChange, () => "German"),
      ],
    },
    problems: [],
  });
}

function matchesFilters(file, query) {
  const classification = query.get("classification");
  const state = query.get("state");
  const needle = (query.get("q") ?? "").toLowerCase();
  return (
    (!classification || file.classification === classification) &&
    (state !== "cleaned" || file.cleaned_at !== null) &&
    (state !== "left_alone" || file.leave_alone) &&
    (!needle ||
      file.path.toLowerCase().includes(needle) ||
      (file.manager_title ?? "").toLowerCase().includes(needle))
  );
}

export function libraryFilesPage(sim, library, query) {
  const all = sim.libraryFiles.list(library.id);
  const sort = SORTERS[query.get("sort")] ? query.get("sort") : "path";
  const direction = query.get("direction") === "desc" ? "desc" : "asc";
  const pageSize = Number(query.get("page_size")) || DEFAULT_PAGE_SIZE;
  const page = Number(query.get("page")) || 1;
  const sorter = SORTERS[sort];
  const filtered = all
    .filter((file) => matchesFilters(file, query))
    .sort(
      (a, b) =>
        (sorter(a) > sorter(b) ? 1 : sorter(a) < sorter(b) ? -1 : 0) *
        (direction === "asc" ? 1 : -1),
    );
  return shaped("LibraryFilesOut", {
    library_id: library.id,
    scan: all.length > 0 ? scanState(sim) : null,
    summary: totalsOf(all),
    filtered: totalsOf(filtered),
    files: filtered.slice((page - 1) * pageSize, page * pageSize),
    total: filtered.length,
    page,
    page_size: pageSize,
    sort,
    direction,
  });
}

/** The library-cleaning settings of a workflow, made on first use from the workflow itself. */
export function libraryMode(sim, library) {
  sim.store.libraryModes[library.id] ??= shaped("LibrarySettingsOut", {
    library_folders: [
      library.media_type === "tv" ? "D:\\Media\\TV" : "D:\\Media\\Movies",
    ],
    library_schedule_enabled: true,
    clean_hardlinked_files: false,
    keep_original_after_clean: false,
    library_rule_set_id: library.rule_set_id ?? null,
    originals_folder: "",
    skip_if_manager_would_redownload: true,
  });
  return sim.store.libraryModes[library.id];
}
