/** The engine's records as the API words them: the same fields, in the same shapes, the server sends. */
import { shaped } from "../openapi/skeleton.mjs";
import { toWire } from "../wire-time.mjs";

/**
 * One download as the files list shows it, with live progress laid over a file being worked on.
 * @param {import("../engine/file.mjs").SimFile} file
 * @param {Record<string, any> | null} live The file's entry in the live-progress frame, when a pass is running.
 */
export function fileOut(file, live) {
  const wire = (ms) => (ms === null ? null : toWire(ms));
  return shaped("ProcessingFileOut", {
    kind: "download",
    id: file.id,
    library_id: file.libraryId,
    library_name: file.libraryName,
    relative_path: file.relativePath,
    status: file.status,
    status_reason: file.statusReason,
    blocked_by_connection: file.blockedBy,
    size_bytes: file.sizeBytes,
    video_codec: file.video.codec,
    video_width: file.video.width,
    video_height: file.video.height,
    audio_track_count: file.plan.removedAudio.length + 1,
    subtitle_track_count: file.plan.removedSubtitles.length + 1,
    duration_seconds: file.durationSeconds,
    direct_play: [],
    progress_percent: live?.percent ?? null,
    progress_message: live?.message ?? null,
    progress_eta_seconds: live?.eta_seconds ?? null,
    progress_status: live?.status ?? null,
    progress_stage: live?.stage ?? null,
    progress_speed: live?.speed ?? null,
    progress_elapsed_seconds: live?.elapsed_seconds ?? null,
    progress_removed_audio: live?.removed_audio ?? null,
    progress_removed_subtitles: live?.removed_subtitles ?? null,
    failure_class: file.failureClass,
    failure_attempts: file.failureAttempts,
    next_retry_at: null,
    output_collision_policy: null,
    output_collision_action: null,
    output_collision_reason: null,
    hold_until: wire(file.holdUntil),
    size_changed_at: wire(file.sizeChangedAt),
    created_at: toWire(file.createdAt),
    updated_at: toWire(file.updatedAt),
    last_seen_at: toWire(file.updatedAt),
    last_attempt_at: wire(file.lastAttemptAt),
    handback: file.handback,
  });
}

/** The module an event type belongs to: the part before its first dot. */
const moduleOf = (type) =>
  type.split(".")[0].replace(/^library$/, "processing");

/**
 * @param {import("../engine/activity-log.mjs").ActivityEvent} event
 */
export function activityItem(event) {
  return shaped("ActivityEventItemOut", {
    id: event.id,
    created_at: toWire(event.createdAt),
    module: moduleOf(event.type),
    event_type: event.type,
    trigger: event.trigger,
    result: event.result,
    library_id: event.libraryId,
    relative_path: event.relativePath,
    run_key: null,
    title: event.title,
    detail: JSON.stringify(event.detail),
  });
}
