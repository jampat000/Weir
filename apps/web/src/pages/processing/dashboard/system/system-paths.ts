import {
  setupTabPath,
  workflowEditorPath,
} from "../../../../lib/settings/setup-areas";

/** Where the System cards send you: the screens that hold what each card summarises. */
export const JOBS_PATH = "/system?tab=logs&show=jobs";
export const SERVER_LOG_PATH = "/system?tab=logs&show=log";
export const BACKUPS_PATH = "/system?tab=backups";
export const ABOUT_PATH = "/system";
export const MANAGERS_PATH = setupTabPath("managers");

export const workflowPath = workflowEditorPath;
