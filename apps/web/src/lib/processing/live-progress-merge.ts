import type { LiveProgressEntry } from "../activity/use-activity-stream-invalidation";
import type { ProcessingFile } from "./files-api";

/**
 * Overlays the freshest progress the live stream has for each file onto the list `GET
 * /api/v1/processing/files` returned (#750). The file list only changes on a database write — the
 * start of a pass, a stage change, or its end — so between those the percent, ETA and message shown
 * would otherwise sit still for as long as a minute. The live values fill that gap, updated about once
 * a second, without needing another fetch. A file the stream says nothing about keeps whatever the file
 * list already had.
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
      progress_status: entry.status,
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
