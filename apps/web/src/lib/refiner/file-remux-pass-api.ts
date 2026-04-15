import { fetchCsrfToken } from "../api/auth-api";
import { apiFetch, readJson } from "../api/client";
import type { RefinerFileRemuxPassManualEnqueueBody, RefinerFileRemuxPassManualEnqueueOut } from "./types";

export const refinerFileRemuxPassEnqueuePath = () => "/api/v1/refiner/jobs/file-remux-pass/enqueue";

export async function postRefinerFileRemuxPassEnqueue(
  body: RefinerFileRemuxPassManualEnqueueBody,
): Promise<RefinerFileRemuxPassManualEnqueueOut> {
  const csrf_token = await fetchCsrfToken();
  const r = await apiFetch(refinerFileRemuxPassEnqueuePath(), {
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
    throw new Error(detail || `Could not queue file pass (${r.status})`);
  }
  return readJson<RefinerFileRemuxPassManualEnqueueOut>(r);
}
