import { apiFetch, readJson, requireOk } from "../api/client";
import { sendJson } from "../api/send-json";
import type { Schema } from "../api/types";
import {
  processingFilesPath,
  withQuery,
  type ProcessingRequeueResult,
} from "./files-api";

/** How many files are rejected, and how many of them can be processed again right now. */
export type RejectedFilesSummary = Schema<"ProcessingRejectedFilesSummaryOut">;

const rejectedFilesPath = () => `${processingFilesPath()}/rejected`;

/** The whole rejected set, or one workflow's when `libraryId` is given. */
export async function fetchRejectedFilesSummary(
  libraryId?: number,
): Promise<RejectedFilesSummary> {
  const path = withQuery(`${rejectedFilesPath()}/summary`, {
    library_id: libraryId,
  });
  const response = await apiFetch(path);
  await requireOk(path, response, "Could not count the rejected files");
  return readJson<RejectedFilesSummary>(response);
}

/** Queues every rejected file whose original is still in its watched folder, under the rules as they are now. */
export async function processRejectedFilesAgain(
  libraryId?: number,
): Promise<ProcessingRequeueResult> {
  const response = await sendJson(
    `${rejectedFilesPath()}/process-again`,
    "POST",
    { library_id: libraryId },
    "Could not process the rejected files again",
  );
  return readJson<ProcessingRequeueResult>(response);
}
