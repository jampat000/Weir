import type { RequestBody, Schema } from "../api/types";

export type ProcessingRuntimeSettingsOut =
  Schema<"ProcessingRuntimeSettingsOut">;
export type ProcessingOverviewStatsOut = Schema<"ProcessingOverviewStatsOut">;
/** What is running, what is waiting, and the one limit the waiting files are waiting on (#633). */
export type ProcessingFilesAtOnceOut = Schema<"ProcessingFilesAtOnceOut">;
export type ProcessingOperatorSettingsOut =
  Schema<"ProcessingOperatorSettingsOut">;

/** Partial PUT: include only the fields to change. */
export type ProcessingOperatorSettingsPutBody =
  RequestBody<"ProcessingOperatorSettingsPutIn">;

export type ProcessingWatchedFolderRemuxScanDispatchEnqueueBody =
  RequestBody<"ProcessingWatchedFolderRemuxScanDispatchManualEnqueueIn">;
export type ProcessingWatchedFolderRemuxScanDispatchEnqueueOut =
  Schema<"ProcessingWatchedFolderRemuxScanDispatchManualEnqueueOut">;

/**
 * Kept by hand: the schema gives pass_through_unchanged a default, which the generator reads as required,
 * but the server fills it in when it is left out.
 */
export type ProcessingFileRemuxPassManualEnqueueBody = {
  relative_media_path: string;
  media_scope: "movie" | "tv";
  library_id?: number;
  pass_through_unchanged?: boolean;
};

export type ProcessingFileRemuxPassManualEnqueueOut =
  Schema<"ProcessingFileRemuxPassManualEnqueueOut">;
