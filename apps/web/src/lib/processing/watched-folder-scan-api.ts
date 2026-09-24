import { sendJson } from "../api/send-json";
import { readJson } from "../api/client";
import type {
  ProcessingWatchedFolderRemuxScanDispatchEnqueueBody,
  ProcessingWatchedFolderRemuxScanDispatchEnqueueOut,
} from "./types";

export const processingWatchedFolderRemuxScanDispatchEnqueuePath = () =>
  "/api/v1/processing/jobs/watched-folder-remux-scan-dispatch/enqueue";

export async function postProcessingWatchedFolderRemuxScanDispatchEnqueue(
  body: ProcessingWatchedFolderRemuxScanDispatchEnqueueBody,
): Promise<ProcessingWatchedFolderRemuxScanDispatchEnqueueOut> {
  const path = processingWatchedFolderRemuxScanDispatchEnqueuePath();
  const r = await sendJson(
    path,
    "POST",
    body,
    "Could not queue watched-folder scan",
  );
  return readJson<ProcessingWatchedFolderRemuxScanDispatchEnqueueOut>(r);
}
