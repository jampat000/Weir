import { FAILED_IMPORT_STATUS_HANDLER_OK_FINALIZE_FAILED } from "./task-status-labels";

/** Admin/operator — edit Fetcher TV/movie automatic search preferences (not credentials). */
export function showFetcherArrOperatorSettingsEditor(role: string | undefined): boolean {
  if (!role) {
    return false;
  }
  return role === "admin" || role === "operator";
}

/** Admin/operator — edit failed-import removal rules (download queue). */
export function showFailedImportCleanupPolicyEditor(role: string | undefined): boolean {
  if (!role) {
    return false;
  }
  return role === "admin" || role === "operator";
}

/** Admin/operator — show manual “add to work queue” controls for Radarr/Sonarr failed-import passes. */
export function showFailedImportManualQueuePassControl(role: string | undefined): boolean {
  if (!role) {
    return false;
  }
  return role === "admin" || role === "operator";
}

/** Admin/operator — manual recover for ``fetcher_jobs`` stuck in ``handler_ok_finalize_failed`` (any job kind). */
export function showFetcherJobRecoverFinalizeControl(role: string | undefined, jobStatus: string): boolean {
  if (jobStatus !== FAILED_IMPORT_STATUS_HANDLER_OK_FINALIZE_FAILED) {
    return false;
  }
  if (!role) {
    return false;
  }
  return role === "admin" || role === "operator";
}
