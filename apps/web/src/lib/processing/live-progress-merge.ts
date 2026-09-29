import type { LiveProgressEntry } from "../activity/use-activity-stream-invalidation";
import type { ProcessingFile } from "./files-api";

const NOT_STARTED_STATUSES: ReadonlySet<ProcessingFile["status"]> = new Set([
  "unprocessed",
  "out_of_schedule",
]);

/**
 * Overlays the freshest progress the live stream has for each file onto the list `GET
 * /api/v1/processing/files` returned (#750). The file list only changes on a database write — the
 * start of a pass, a stage change, or its end — so between those the percent, ETA and message shown
 * would otherwise sit still for as long as a minute. The live values fill that gap, updated about once
 * a second, without needing another fetch. A file the stream says nothing about keeps whatever the file
 * list already had.
 *
 * A pass only starts on a file the list calls Waiting, and the list learns of the claim on its next refresh. A
 * progress frame for such a file proves the pass has started, so the file is shown as processing at once and
 * moves from Waiting to Working in one step, instead of being on neither until the refresh.
 */
export function mergeLiveProgress(
  files: ProcessingFile[],
  live: Readonly<Record<string, LiveProgressEntry>>,
): ProcessingFile[] {
  if (Object.keys(live).length === 0) return files;
  return files.map((file) => {
    const entry = live[file.relative_path];
    if (!entry) return file;
    return {
      ...file,
      status: NOT_STARTED_STATUSES.has(file.status)
        ? "processing"
        : file.status,
      progress_status: entry.status,
      progress_stage: entry.stage,
      progress_percent: entry.percent,
      progress_eta_seconds: entry.etaSeconds,
      progress_message: entry.message,
      progress_speed: entry.speed,
      progress_elapsed_seconds: entry.elapsedSeconds,
      progress_removed_audio: entry.removedAudio,
      progress_removed_subtitles: entry.removedSubtitles,
    };
  });
}
