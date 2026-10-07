/** Activity event types the web app reads structure from. They match the server's names exactly. */
export const FILE_PROGRESS_EVENT = "processing.file_processing_progress";
export const REMUX_PASS_COMPLETED_EVENT =
  "processing.file_remux_pass_completed";
export const LIBRARY_FILE_CLEANED_EVENT = "library.file_cleaned";
/** A media manager's word on whether it imported the copy Weir handed back. */
export const HANDBACK_OUTCOME_EVENT = "processing.handback_outcome";
/** A repeat of a file Weir already cleaned, left alone. The server's title says "already done" or "already imported". */
export const SKIPPED_REPEAT_EVENT = "processing.file_skipped_repeat";
