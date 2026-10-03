/**
 * The files already sitting in each workflow's library, as the Library page lists them, and which of them Weir has
 * cleaned in place. Titles are the simulation's own.
 */
import {
  FILMS,
  FILMS_4K,
  FILMS_KIDS,
  SHOWS,
  filmPosterId,
  showPosterId,
} from "./catalogue.mjs";
import {
  FOUR_K_LIBRARY_ID,
  KIDS_LIBRARY_ID,
  MOVIES_LIBRARY_ID,
  TV_LIBRARY_ID,
} from "../fixtures/workflows.mjs";

const GIGABYTE = 1024 ** 3;
const DAY_SECONDS = 86_400;
const EPISODES_PER_SHOW = 5;
const SHARE_PER_REMOVED_AUDIO_TRACK = 0.04;

const MOVIES_ROOT = "D:\\Media\\Movies";
const TV_ROOT = "D:\\Media\\TV";
const KIDS_ROOT = "D:\\Media\\Kids";
const FOUR_K_ROOT = "D:\\Media\\4K Movies";

/** Out of every ten library files, this many already match the rules. */
const MATCHING_OUT_OF_TEN = 4;

/**
 * Among the files the rules would change, the reason the scan gives for each in turn: it is new, it was replaced, the
 * rules changed, or nothing on record says. The fourth is the honest answer for a file that was there before the
 * scan started keeping reasons.
 */
const CHANGE_REASONS = ["new", "replaced", "rules_changed", null];

/** Every seventh file is still shared with a download, every eleventh Weir cannot read, every thirteenth is set aside. */
const SEEDING_EVERY = 7;
const UNREADABLE_EVERY = 11;
const LEFT_ALONE_EVERY = 13;

/** @typedef {Record<string, any>} LibraryFile */

function entry({
  path,
  title,
  posterId,
  kind,
  sizeBytes,
  height,
  removedAudio,
  removedSubtitles,
  ageDays,
  nowSeconds,
  index,
}) {
  const unreadable = index % UNREADABLE_EVERY === 5;
  const seeding = index % SEEDING_EVERY === 3;
  const matches = unreadable || (removedAudio === 0 && removedSubtitles === 0);
  if (unreadable) {
    removedAudio = 0;
    removedSubtitles = 0;
  }
  return {
    path,
    poster_id: posterId,
    size_bytes: sizeBytes,
    modified_at: nowSeconds - ageDays * DAY_SECONDS,
    classification: unreadable
      ? "cannot_process"
      : matches
        ? "matches"
        : "would_change",
    summary: null,
    reason: unreadable ? "Weir could not read this file." : null,
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
    link_count: seeding && !matches ? 2 : 1,
    problem_kind: unreadable ? "unreadable" : null,
    cleaned_at: null,
    leave_alone: index % LEFT_ALONE_EVERY === 7,
    // Why the scan thinks it needs cleaning: kept here, and said on the wire only for a file that does.
    change_reason: matches
      ? null
      : CHANGE_REASONS[index % CHANGE_REASONS.length],
  };
}

function removals(index) {
  const matches = index % 10 < MATCHING_OUT_OF_TEN;
  return matches
    ? { removedAudio: 0, removedSubtitles: 0 }
    : { removedAudio: 1 + (index % 3), removedSubtitles: 2 + (index % 5) };
}

/**
 * @param {readonly import("./catalogue.mjs").FilmEntry[]} films
 * @param {{ root: string, kind: string | null, nowSeconds: number }} where `kind` is the manager that knows the titles, or null for a workflow without one.
 */
function filmEntries(films, { root, kind, nowSeconds }) {
  return films.map((film, index) =>
    entry({
      path: `${root}\\${film.title} (${film.year})\\${film.title} (${film.year}) [Bluray-${film.resolution}p].mkv`,
      title: kind === null ? null : film.title,
      posterId: filmPosterId(film),
      kind,
      sizeBytes: Math.round(film.gigabytes * GIGABYTE),
      height: film.resolution,
      ageDays: 9 + index,
      nowSeconds,
      index,
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
        posterId: showPosterId(show),
        kind: "sonarr",
        sizeBytes: Math.round(show.gigabytes * GIGABYTE),
        height: show.resolution,
        ageDays: 5 + showIndex * 3 + episodeIndex,
        nowSeconds,
        index: showIndex * EPISODES_PER_SHOW + episodeIndex,
        ...removals(showIndex * EPISODES_PER_SHOW + episodeIndex),
      });
    }),
  );
}

/**
 * Where a file stands now, against the current rules: one status, so the counts add up to the files. Left alone beats
 * cleaning, which beats can't clean yet (still shared with a download, or unreadable), which beats needs cleaning, which
 * beats matches.
 * @param {LibraryFile} file
 * @param {{ queuedPaths: ReadonlySet<string>, cleansHardlinked: boolean }} now
 */
export function statusOf(file, { queuedPaths, cleansHardlinked }) {
  if (file.leave_alone) return "left_alone";
  if (queuedPaths.has(file.path)) return "cleaning";
  const shared = !cleansHardlinked && (file.link_count ?? 1) > 1;
  if (
    file.classification === "cannot_process" ||
    (file.classification === "would_change" &&
      (file.problem_kind !== null || shared))
  )
    return "cant_clean_yet";
  return file.classification === "would_change" ? "needs_cleaning" : "matches";
}

/**
 * The sums the Library page's cards show for a list of files.
 * @param {LibraryFile[]} files
 * @param {{ queuedPaths: ReadonlySet<string>, cleansHardlinked: boolean }} now
 */
export function totalsOf(files, now) {
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
    by_status: {
      needs_cleaning: 0,
      cleaning: 0,
      matches: 0,
      cant_clean_yet: 0,
      left_alone: 0,
    },
  };
  for (const file of files) {
    totals.by_status[statusOf(file, now)] += 1;
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
      [
        MOVIES_LIBRARY_ID,
        filmEntries(FILMS, { root: MOVIES_ROOT, kind: "radarr", nowSeconds }),
      ],
      [TV_LIBRARY_ID, tvEntries(nowSeconds)],
      [
        KIDS_LIBRARY_ID,
        filmEntries(FILMS_KIDS, { root: KIDS_ROOT, kind: null, nowSeconds }),
      ],
      [
        FOUR_K_LIBRARY_ID,
        filmEntries(FILMS_4K, {
          root: FOUR_K_ROOT,
          kind: "radarr",
          nowSeconds,
        }),
      ],
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
          file.link_count <= 1 &&
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
    file.change_reason = null;
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
