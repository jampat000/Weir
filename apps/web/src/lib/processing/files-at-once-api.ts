import { apiFetch, readJson, requireOk } from "../api/client";
import type { ProcessingFilesAtOnceOut } from "./types";

export const processingFilesAtOncePath = () =>
  "/api/v1/processing/files-at-once";

/** What is running, what is waiting, and the one limit the waiting files are waiting on (#633). */
export async function fetchProcessingFilesAtOnce(): Promise<ProcessingFilesAtOnceOut> {
  const path = processingFilesAtOncePath();
  const r = await apiFetch(path);
  await requireOk(path, r, "Could not load what is running");
  return readJson<ProcessingFilesAtOnceOut>(r);
}
