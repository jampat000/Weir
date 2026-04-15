import { fetchCsrfToken } from "../../api/auth-api";
import { apiFetch, readJson } from "../../api/client";
import type {
  FetcherArrConnectionPutBody,
  FetcherArrConnectionTestBody,
  FetcherArrConnectionTestOut,
  FetcherArrOperatorSettingsOut,
  FetcherArrOperatorSettingsPutBody,
  FetcherArrSearchLane,
  FetcherArrSearchLaneKey,
} from "./types";

export const fetcherArrOperatorSettingsPath = () => "/api/v1/fetcher/arr-operator-settings";

export async function fetchFetcherArrOperatorSettings(): Promise<FetcherArrOperatorSettingsOut> {
  const r = await apiFetch(fetcherArrOperatorSettingsPath());
  if (!r.ok) {
    throw new Error(`Could not load TV and movie search settings (${r.status})`);
  }
  return readJson<FetcherArrOperatorSettingsOut>(r);
}

export async function putFetcherArrOperatorSettings(
  body: FetcherArrOperatorSettingsPutBody,
): Promise<FetcherArrOperatorSettingsOut> {
  const csrf_token = await fetchCsrfToken();
  const r = await apiFetch(fetcherArrOperatorSettingsPath(), {
    method: "PUT",
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
    throw new Error(detail || `Could not save settings (${r.status})`);
  }
  return readJson<FetcherArrOperatorSettingsOut>(r);
}

export async function putFetcherArrSearchLane(
  laneKey: FetcherArrSearchLaneKey,
  lane: FetcherArrSearchLane,
): Promise<FetcherArrOperatorSettingsOut> {
  const csrf_token = await fetchCsrfToken();
  const r = await apiFetch(`${fetcherArrOperatorSettingsPath()}/lanes/${laneKey}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ csrf_token, lane }),
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
    throw new Error(detail || `Could not save this search lane (${r.status})`);
  }
  return readJson<FetcherArrOperatorSettingsOut>(r);
}

async function putFetcherArrConnection(
  path: string,
  body: FetcherArrConnectionPutBody,
): Promise<FetcherArrOperatorSettingsOut> {
  const csrf_token = await fetchCsrfToken();
  const r = await apiFetch(path, {
    method: "PUT",
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
    throw new Error(detail || `Could not save connection (${r.status})`);
  }
  return readJson<FetcherArrOperatorSettingsOut>(r);
}

export async function putFetcherArrConnectionSonarr(
  body: FetcherArrConnectionPutBody,
): Promise<FetcherArrOperatorSettingsOut> {
  return putFetcherArrConnection("/api/v1/fetcher/arr-connection/sonarr", body);
}

export async function putFetcherArrConnectionRadarr(
  body: FetcherArrConnectionPutBody,
): Promise<FetcherArrOperatorSettingsOut> {
  return putFetcherArrConnection("/api/v1/fetcher/arr-connection/radarr", body);
}

export async function postFetcherArrConnectionTest(body: FetcherArrConnectionTestBody): Promise<FetcherArrConnectionTestOut> {
  const { app, enabled, base_url, api_key } = body;
  const csrf_token = await fetchCsrfToken();
  const r = await apiFetch(`${fetcherArrOperatorSettingsPath()}/connection-test`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ csrf_token, app, enabled, base_url, api_key }),
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
    throw new Error(detail || `Connection check failed (${r.status})`);
  }
  return readJson<FetcherArrConnectionTestOut>(r);
}
