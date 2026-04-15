import { apiFetch, readJson } from "../../api/client";
import type { FetcherFailedImportQueueAttentionSnapshot } from "./types";

export const failedImportQueueAttentionSnapshotPath = () =>
  "/api/v1/fetcher/failed-imports/queue-attention-snapshot";

export async function fetchFailedImportQueueAttentionSnapshot(): Promise<FetcherFailedImportQueueAttentionSnapshot> {
  const r = await apiFetch(failedImportQueueAttentionSnapshotPath());
  if (!r.ok) {
    throw new Error(`Could not load failed-import queue attention (${r.status})`);
  }
  return readJson<FetcherFailedImportQueueAttentionSnapshot>(r);
}
