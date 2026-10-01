/**
 * What Weir decides to do with a simulated file: which tracks go, how big the result is, and whether the rules turn
 * it away. The words match the planner's own, so History reads the way it does against a real server.
 */
import { FOREIGN_AUDIO, SUBTITLE_LANGUAGES } from "./catalogue.mjs";

/** The decisions a pass can reach. */
export const VERDICT = Object.freeze({
  CLEAN: "clean",
  ALREADY_RIGHT: "already_right",
  REJECTED: "rejected",
  FAILS: "fails",
});

const KEPT_AUDIO = "English 5.1 AC-3";
const SUBTITLE_NAMES = {
  fre: "French",
  ger: "German",
  ita: "Italian",
  spa: "Spanish",
  jpn: "Japanese",
  por: "Portuguese",
  dut: "Dutch",
  swe: "Swedish",
  nor: "Norwegian",
  dan: "Danish",
};
/** What dropping one foreign audio track and one subtitle track each shave off a file, as a share of its size. */
const SHARE_PER_AUDIO_TRACK = 0.045;
const SHARE_PER_SUBTITLE_TRACK = 0.003;
const FIXED_SAVING_SHARE = 0.01;
const MOST_AUDIO_TRACKS_REMOVED = 4;
const MOST_SUBTITLES_REMOVED = 8;

/**
 * @typedef {object} Plan
 * @property {string} verdict One of {@link VERDICT}.
 * @property {string[]} removedAudio One plan line per audio track taken out.
 * @property {string[]} removedSubtitles One plan line per subtitle track taken out.
 * @property {string} audioBefore
 * @property {string} audioAfter
 * @property {string} subsBefore
 * @property {string} subsAfter
 * @property {number | null} outputBytes The size of the written copy; null when nothing is written.
 * @property {number | null} failsAtPercent Where a pass that fails stops writing.
 * @property {string | null} rejectionReason
 */

const audioRemovalLine = (track) =>
  `${track} (not selected — ${KEPT_AUDIO} kept)`;
const subtitleRemovalLine = (code) =>
  `${SUBTITLE_NAMES[code] ?? code} (not selected — English kept)`;

function pickSome(rng, items, count) {
  const pool = [...items];
  const chosen = [];
  while (chosen.length < count && pool.length > 0) {
    chosen.push(pool.splice(rng.int(0, pool.length - 1), 1)[0]);
  }
  return chosen;
}

/**
 * @param {import("./rng.mjs").Rng} rng
 * @param {string} verdict
 * @param {number} sizeBytes
 * @returns {Plan}
 */
export function makePlan(rng, verdict, sizeBytes) {
  const empty = {
    verdict,
    removedAudio: [],
    removedSubtitles: [],
    audioBefore: KEPT_AUDIO,
    audioAfter: KEPT_AUDIO,
    subsBefore: "English",
    subsAfter: "English",
    outputBytes: null,
    failsAtPercent: null,
    rejectionReason: null,
  };
  if (verdict === VERDICT.ALREADY_RIGHT) return empty;
  if (verdict === VERDICT.REJECTED) {
    return {
      ...empty,
      audioBefore: "Japanese 2.0 AAC · French 5.1 AC-3",
      audioAfter: "",
      rejectionReason: "This file has no English audio track.",
    };
  }
  const audio = pickSome(
    rng,
    FOREIGN_AUDIO,
    rng.int(1, MOST_AUDIO_TRACKS_REMOVED),
  );
  const subtitles = pickSome(
    rng,
    SUBTITLE_LANGUAGES,
    rng.int(2, MOST_SUBTITLES_REMOVED),
  );
  const savedShare =
    FIXED_SAVING_SHARE +
    audio.length * SHARE_PER_AUDIO_TRACK +
    subtitles.length * SHARE_PER_SUBTITLE_TRACK;
  return {
    ...empty,
    removedAudio: audio.map(audioRemovalLine),
    removedSubtitles: subtitles.map(subtitleRemovalLine),
    audioBefore: [KEPT_AUDIO, ...audio].join(" · "),
    subsBefore: [
      "English",
      ...subtitles.map((code) => SUBTITLE_NAMES[code] ?? code),
    ].join(" · "),
    outputBytes: Math.round(sizeBytes * (1 - savedShare)),
    failsAtPercent: verdict === VERDICT.FAILS ? rng.int(25, 85) : null,
  };
}
