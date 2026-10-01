/**
 * The files already sitting in each workflow's library, as the Library page lists them, and which of them Weir has
 * cleaned in place. Titles are the simulation's own.
 */
import { FILMS, SHOWS } from "./catalogue.mjs";
import { MOVIES_LIBRARY_ID, TV_LIBRARY_ID } from "../fixtures/workflows.mjs";

const GIGABYTE = 1024 ** 3;
const DAY_SECONDS = 86_400;
const EPISODES_PER_SHOW = 5;
const SHARE_PER_REMOVED_AUDIO_TRACK = 0.04;

const MOVIES_ROOT = "D:\\Media\\Movies";
const TV_ROOT = "D:\\Media\\TV";

/** Out of every ten library files, this many already match the rules. */
const MATCHING_OUT_OF_TEN = 4;

/** @typedef {Record<string, any>} LibraryFile */

function entry({
  path,
  title,
  kind,
  sizeBytes,
  height,
  removedAudio,
  removedSubtitles,
  ageDays,
  nowSeconds,
}) {
  const matches = removedAudio === 0 && removedSubtitles === 0;
  return {
    path,
    size_bytes: sizeBytes,
    modified_at: nowSeconds - ageDays * DAY_SECONDS,
    classification: matches ? "matches" : "would_change",
    summary: null,
    reason: null,
    removed_audio_tracks: removedAudio,
    removed_subtitle_tracks: removedSubtitles,
    estimated_bytes_saved: matches
      ? 0
      : Math.round(
          sizeBytes * (0.01 + removedAudio * SHARE_PER_REMOVED_AUDIO_TRACK),
        ),
    manager_kind: kind,
    manager_title: title,
    video_codec: height >= 2160 ? "hevc" : "h264",
    video_height: height,
    resolution_class: `${height}p`,
    audio_track_count: removedAudio + 1,
    subtitle_track_count: removedSubtitles + 1,
    audio_summary:
      removedAudio === 0
        ? "eng ac3 5.1"
        : `eng ac3 5.1, fre ac3 5.1${removedAudio > 1 ? ` +${removedAudio - 1} more` : ""}`,
    subtitle_summary:
      removedSubtitles === 0
        ? "eng"
        : `eng, fre, ger${removedSubtitles > 2 ? ` +${removedSubtitles - 2} more` : ""}`,
    link_count: 1,
    problem_kind: null,
    cleaned_at: null,
    leave_alone: false,
  };
}

function removals(index) {
  const matches = index % 10 < MATCHING_OUT_OF_TEN;
  return matches
    ? { removedAudio: 0, removedSubtitles: 0 }
    : { removedAudio: 1 + (index % 3), removedSubtitles: 2 + (index % 5) };
}

function movieEntries(nowSeconds) {
  return FILMS.map((film, index) =>
    entry({
      path: `${MOVIES_ROOT}\\${film.title} (${film.year})\\${film.title} (${film.year}) [Bluray-${film.resolution}p].mkv`,
      title: film.title,
      kind: "radarr",
      sizeBytes: Math.round(film.gigabytes * GIGABYTE),
      height: film.resolution,
      ageDays: 9 + index,
      nowSeconds,
      ...removals(index),
    }),
  );
}

function tvEntries(nowSeconds) {
  return SHOWS.flatMap((show, showIndex) =>
    Array.from({ length: EPISODES_PER_SHOW }, (_, episodeIndex) => {
      const season = String(show.season).padStart(2, "0");
      const code = `S${season}E${String(episodeIndex + 1).padStart(2, "0")}`;
      return entry({
        path: `${TV_ROOT}\\${show.title}\\Season ${season}\\${show.title} - ${code}.mkv`,
        title: show.title,
        kind: "sonarr",
        sizeBytes: Math.round(show.gigabytes * GIGABYTE),
        height: show.resolution,
        ageDays: 5 + showIndex * 3 + episodeIndex,
        nowSeconds,
        ...removals(showIndex * EPISODES_PER_SHOW + episodeIndex),
      });
    }),
  );
}

/** The sums the Library page's cards show for a list of files. */
export function totalsOf(files) {
  const totals = {
    files: files.length,
    size_bytes: 0,
    matches: 0,
    would_change: 0,
    cannot_process: 0,
    cleaned: 0,
    left_alone: 0,
    total_removed_audio_tracks: 0,
    total_removed_subtitle_tracks: 0,
    estimated_bytes_saved: 0,
  };
  for (const file of files) {
    totals.size_bytes += file.size_bytes;
    totals[file.classification] += 1;
    totals.total_removed_audio_tracks += file.removed_audio_tracks;
    totals.total_removed_subtitle_tracks += file.removed_subtitle_tracks;
    totals.estimated_bytes_saved += file.estimated_bytes_saved;
    if (file.cleaned_at) totals.cleaned += 1;
    if (file.leave_alone) totals.left_alone += 1;
  }
  return totals;
}

export class LibraryFiles {
  /** @type {Map<number, LibraryFile[]>} */
  #byLibrary;

  /** @param {number} nowMs */
  constructor(nowMs) {
    const nowSeconds = Math.floor(nowMs / 1000);
    this.#byLibrary = new Map([
      [MOVIES_LIBRARY_ID, movieEntries(nowSeconds)],
      [TV_LIBRARY_ID, tvEntries(nowSeconds)],
    ]);
  }

  /** @param {number} libraryId */
  list(libraryId) {
    return this.#byLibrary.get(libraryId) ?? [];
  }

  /** The next file a library clean would change and nobody has asked to leave alone, or null. */
  nextToClean(libraryId, queuedPaths) {
    return (
      this.list(libraryId).find(
        (file) =>
          file.classification === "would_change" &&
          !file.cleaned_at &&
          !file.leave_alone &&
          !queuedPaths.has(file.path),
      ) ?? null
    );
  }

  /**
   * @param {number} libraryId
   * @param {string} path
   * @param {number} nowMs
   */
  markCleaned(libraryId, path, nowMs) {
    const file = this.list(libraryId).find(
      (candidate) => candidate.path === path,
    );
    if (!file) return;
    file.cleaned_at = Math.floor(nowMs / 1000);
    file.classification = "matches";
    file.estimated_bytes_saved = 0;
    file.removed_audio_tracks = 0;
    file.removed_subtitle_tracks = 0;
  }

  /**
   * @param {number} libraryId
   * @param {string[]} paths
   * @param {boolean} leaveAlone
   */
  setLeaveAlone(libraryId, paths, leaveAlone) {
    for (const file of this.list(libraryId)) {
      if (paths.includes(file.path)) file.leave_alone = leaveAlone;
    }
  }
}
