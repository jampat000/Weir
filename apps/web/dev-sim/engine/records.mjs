/**
 * What Weir writes down when a pass ends: the Activity entry's detail and the plain-language story History tells
 * from it. Field names are the server's, so every History and Processing screen reads them as it would from a real
 * Weir.
 */
import { VERDICT } from "./plan.mjs";
import { toWire } from "../wire-time.mjs";

export const EVENT_TYPE = Object.freeze({
  PASS_COMPLETED: "processing.file_remux_pass_completed",
  PASS_PROGRESS: "processing.file_processing_progress",
  LIBRARY_FILE_CLEANED: "library.file_cleaned",
  HANDBACK_OUTCOME: "processing.handback_outcome",
});

const GIGABYTE = 1024 ** 3;
const REJECTION_CLEANUP = "The file was left where it is.";
const ACCEPT_HINT =
  'To accept files like this, change the first-choice language or "How to choose audio" in Settings › Rules.';
const FAILURE_REASON =
  "ffmpeg stopped part-way through writing the file, so Weir threw the half-written copy away.";

/** @param {number} bytes */
const gigabytes = (bytes) => `${(bytes / GIGABYTE).toFixed(1)} GB`;

function cleanDetail(file, nowMs) {
  const { plan } = file;
  return {
    outcome: "live_output_written",
    ok: true,
    remux_required: true,
    source_size_bytes: file.sizeBytes,
    output_size_bytes: plan.outputBytes,
    removed_audio: plan.removedAudio,
    removed_subtitles: plan.removedSubtitles,
    audio_before: plan.audioBefore,
    audio_after: plan.audioAfter,
    subs_before: plan.subsBefore,
    subs_after: plan.subsAfter,
    plan_summary: `Remove ${plan.removedAudio.length} audio and ${plan.removedSubtitles.length} subtitle tracks. The video is copied, not re-encoded.`,
    output_file: file.handback?.output_path ?? "",
    elapsed_seconds: Math.max(
      1,
      Math.round((nowMs - file.passStartedAt) / 1000),
    ),
  };
}

function alreadyRightDetail(file) {
  return {
    outcome: "live_skipped_not_required",
    ok: true,
    remux_required: false,
    source_size_bytes: file.sizeBytes,
    audio_before: file.plan.audioBefore,
    audio_after: file.plan.audioAfter,
    subs_before: file.plan.subsBefore,
    subs_after: file.plan.subsAfter,
    plan_summary:
      "The file already matches your rules, so Weir handed it back as it was.",
  };
}

/**
 * The detail of a finished pass: one object, kept on the Activity entry and in the file's own log.
 * @param {import("./file.mjs").SimFile} file
 * @param {number} nowMs
 */
export function passDetail(file, nowMs) {
  const base = {
    media_scope: file.mediaType,
    relative_media_path: file.relativePath,
  };
  switch (file.plan.verdict) {
    case VERDICT.CLEAN:
      return { ...base, ...cleanDetail(file, nowMs) };
    case VERDICT.ALREADY_RIGHT:
      return { ...base, ...alreadyRightDetail(file) };
    case VERDICT.REJECTED:
      return {
        ...base,
        outcome: "live_rejected",
        ok: true,
        rejected_without_manager: true,
        reason: file.plan.rejectionReason,
        rejected_cleanup_detail: REJECTION_CLEANUP,
        audio_before: file.plan.audioBefore,
        source_size_bytes: file.sizeBytes,
      };
    default:
      return {
        ...base,
        outcome: "failed_ffmpeg",
        ok: false,
        reason: FAILURE_REASON,
        source_size_bytes: file.sizeBytes,
      };
  }
}

/** The sentence a file's status carries once its pass has ended. */
export function statusReasonFor(file) {
  switch (file.plan.verdict) {
    case VERDICT.CLEAN:
      return `Weir removed ${file.plan.removedAudio.length} audio and ${file.plan.removedSubtitles.length} subtitle tracks and handed the copy back.`;
    case VERDICT.ALREADY_RIGHT:
      return "Already matches your rules, so Weir handed it back as it was.";
    case VERDICT.REJECTED:
      return `Rejected: ${file.plan.rejectionReason} ${ACCEPT_HINT} ${REJECTION_CLEANUP}`;
    default:
      return FAILURE_REASON;
  }
}

const step = (heading, sentence, tone = "neutral") => ({
  heading,
  sentence,
  tone,
});

function pickedUp(file) {
  const kind = file.mediaType === "tv" ? "TV episode" : "film";
  return step(
    "Picked up",
    `Weir took this file as a ${kind} in the ${file.libraryName} workflow.`,
  );
}

function lookedInside(file) {
  const audio = file.plan.removedAudio.length + 1;
  const subtitles = file.plan.removedSubtitles.length + 1;
  return step(
    "Looked inside",
    `It found 1 video track, ${audio} audio tracks and ${subtitles} subtitle tracks.`,
  );
}

/**
 * The plain-language story of a finished pass.
 * @param {import("./file.mjs").SimFile} file
 * @param {Record<string, any>} detail
 */
function storyOf(file, detail) {
  const opening = [pickedUp(file), lookedInside(file)];
  switch (file.plan.verdict) {
    case VERDICT.CLEAN:
      return [
        ...opening,
        step(
          "Planned",
          `Weir planned to remove ${detail.removed_audio.length} audio and ${detail.removed_subtitles.length} subtitle tracks. The video is copied, not re-encoded, so picture quality is unchanged.`,
        ),
        step(
          "Worked",
          `${gigabytes(detail.source_size_bytes)} became ${gigabytes(detail.output_size_bytes)}.`,
          "good",
        ),
        step(
          "Checked",
          "Weir checked the finished file was complete before handing it on.",
          "good",
        ),
        step(
          "Handed back",
          "Weir put the cleaned copy in the hand-back folder for your media manager.",
          "good",
        ),
      ];
    case VERDICT.ALREADY_RIGHT:
      return [
        ...opening,
        step(
          "Planned",
          "Nothing needed to come out: it already matches your rules.",
          "good",
        ),
        step("Handed back", "Weir handed the file back as it was.", "good"),
      ];
    case VERDICT.REJECTED:
      return [
        ...opening,
        step(
          "Decided",
          `${detail.reason} Your rules say to turn it away, so Weir did not clean it.`,
          "warn",
        ),
        step("Left alone", REJECTION_CLEANUP, "neutral"),
      ];
    default:
      return [
        ...opening,
        step("Worked", detail.reason, "bad"),
        step(
          "Left alone",
          "The original is untouched, so nothing was lost.",
          "neutral",
        ),
      ];
  }
}

/**
 * One entry of a file's processing record, as the file-log endpoint lists it.
 * @param {import("./file.mjs").SimFile} file
 * @param {number} id
 * @param {number} nowMs
 */
export function logEntryFor(file, id, nowMs) {
  const detail = file.outcomeDetail ?? passDetail(file, nowMs);
  return {
    id,
    recorded_at: toWire(file.finishedAt ?? nowMs),
    outcome: detail.outcome,
    title: "",
    library_name: file.libraryName,
    detail,
    story: storyOf(file, detail),
  };
}
