import { fetchCsrfToken } from "../api/auth-api";
import { apiFetch, readJson, requireOk } from "../api/client";
import type { RequestBody } from "../api/types";

/** Kept by hand: the server always sends paused_until, which the schema marks optional. */
export interface PauseState {
  paused: boolean;
  /** When the pause lifts on its own. Null for one that lasts until it is lifted by hand. */
  paused_until: string | null;
  scan_while_paused: boolean;
  reason: string;
  /** What happens to work already running. Shown, not assumed. */
  in_flight_policy: string;
}

export type PauseWrite = RequestBody<"PauseIn">;

export const pausePath = () => "/api/v1/pause";

export async function fetchPause(): Promise<PauseState> {
  const path = pausePath();
  const response = await apiFetch(path);
  await requireOk(
    path,
    response,
    "Could not read whether processing is paused",
  );
  return readJson<PauseState>(response);
}

export async function savePause(body: PauseWrite): Promise<PauseState> {
  const csrf_token = await fetchCsrfToken();
  const path = pausePath();
  const response = await apiFetch(path, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ ...body, csrf_token }),
  });
  await requireOk(
    path,
    response,
    "Could not change whether processing is paused",
  );
  return readJson<PauseState>(response);
}
