import { fetchCsrfToken } from "../api/auth-api";
import { apiFetch, readJson } from "../api/client";
import type {
  RefinerWatchedFolderRemuxScanDispatchEnqueueBody,
  RefinerWatchedFolderRemuxScanDispatchEnqueueOut,
} from "./types";

export const refinerWatchedFolderRemuxScanDispatchEnqueuePath = () =>
  "/api/v1/refiner/jobs/watched-folder-remux-scan-dispatch/enqueue";

export async function postRefinerWatchedFolderRemuxScanDispatchEnqueue(
  body: RefinerWatchedFolderRemuxScanDispatchEnqueueBody,
): Promise<RefinerWatchedFolderRemuxScanDispatchEnqueueOut> {
  const csrf_token = await fetchCsrfToken();
  const r = await apiFetch(refinerWatchedFolderRemuxScanDispatchEnqueuePath(), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ ...body, csrf_token }),
  });
  if (!r.ok) {
    let detail = r.statusText;
    try {
      const b = await readJson<{ detail?: string }>(r);
      if (typeof b.detail === "string") {
        detail = b.detail;
      }
    } catch {
      /* ignore */
    }
    throw new Error(detail || `Could not queue watched-folder scan (${r.status})`);
  }
  return readJson<RefinerWatchedFolderRemuxScanDispatchEnqueueOut>(r);
}
