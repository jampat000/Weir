import { sendJson } from "../api/send-json";
import { apiFetch, readJson, requireOk } from "../api/client";

/**
 * The five bare download clients Weir can read a watched-folder suggestion from when there is no
 * Sonarr/Radarr/Deluno to ask instead (#768). This connection is outbound only: Weir reads the
 * client's own configuration to suggest a folder, never controls it and never applies anything
 * automatically.
 */
export type DownloadClientKind =
  "sabnzbd" | "nzbget" | "qbittorrent" | "deluge" | "transmission";

export const DOWNLOAD_CLIENT_KIND_LABELS: Record<DownloadClientKind, string> = {
  sabnzbd: "SABnzbd",
  nzbget: "NZBGet",
  qbittorrent: "qBittorrent",
  deluge: "Deluge",
  transmission: "Transmission",
};

/** Which credential fields a kind's own login actually uses, so the form only asks for those. */
export const DOWNLOAD_CLIENT_KIND_CREDENTIALS: Record<
  DownloadClientKind,
  "api_key" | "username_password" | "password_only"
> = {
  sabnzbd: "api_key",
  nzbget: "username_password",
  qbittorrent: "username_password",
  deluge: "password_only",
  transmission: "username_password",
};

/**
 * Hand-written to match the server's plain JSON (no generated OpenAPI type exists for this route
 * yet). Secrets are reported only as saved or not, never their value.
 */
export interface DownloadClientConnection {
  id: number;
  kind: DownloadClientKind;
  name: string;
  enabled: boolean;
  base_url: string;
  username: string | null;
  password_is_saved: boolean;
  api_key_is_saved: boolean;
  last_test_ok: boolean | null;
  last_test_at: string | null;
  last_test_detail: string | null;
}

export interface DownloadClientConnectionCreate {
  kind: DownloadClientKind;
  name: string;
  base_url: string;
  username?: string;
  password?: string;
  api_key?: string;
  enabled?: boolean;
}

/** Leave a secret out to keep the saved one; send "" to clear it. */
export interface DownloadClientConnectionUpdate {
  name?: string;
  base_url?: string;
  username?: string;
  password?: string;
  api_key?: string;
  enabled?: boolean;
}

export interface DownloadClientConnectionTest {
  connection_id: number;
  ok: boolean;
  detail: string;
  checked_at: string;
}

export interface DownloadClientCategoryFolder {
  category: string;
  folder: string;
}

export interface DownloadClientSuggestionLine {
  state: "ok" | "problem" | "note";
  text: string;
}

export interface DownloadClientSuggestion {
  connection_id: number;
  kind: DownloadClientKind;
  name: string;
  label: string;
  flow: "download_client";
  ready: boolean;
  lines: DownloadClientSuggestionLine[];
  suggested_watched_folder: string | null;
  category_folders: DownloadClientCategoryFolder[];
}

export const downloadClientConnectionsPath = () =>
  "/api/v1/download-clients/connections";

const connectionPath = (id: number) =>
  `${downloadClientConnectionsPath()}/${id}`;

export async function fetchDownloadClientConnections(): Promise<
  DownloadClientConnection[]
> {
  const path = downloadClientConnectionsPath();
  const r = await apiFetch(path);
  await requireOk(path, r, "Could not load download clients");
  return readJson<DownloadClientConnection[]>(r);
}

export async function createDownloadClientConnection(
  data: DownloadClientConnectionCreate,
): Promise<DownloadClientConnection> {
  const path = downloadClientConnectionsPath();
  const r = await sendJson(
    path,
    "POST",
    data,
    "Could not add that download client",
  );
  return readJson<DownloadClientConnection>(r);
}

export async function updateDownloadClientConnection(
  id: number,
  data: DownloadClientConnectionUpdate,
): Promise<DownloadClientConnection> {
  const path = connectionPath(id);
  const r = await sendJson(
    path,
    "PUT",
    data,
    "Could not save that download client",
  );
  return readJson<DownloadClientConnection>(r);
}

export async function deleteDownloadClientConnection(
  id: number,
): Promise<void> {
  const path = connectionPath(id);
  await sendJson(path, "DELETE", {}, "Could not remove that download client");
}

export async function testDownloadClientConnection(
  id: number,
): Promise<DownloadClientConnectionTest> {
  const path = `${connectionPath(id)}/test`;
  const r = await sendJson(
    path,
    "POST",
    {},
    "Could not test that download client",
  );
  return readJson<DownloadClientConnectionTest>(r);
}

/**
 * `media_type` is required by the route for consistency with the media-manager setup check, but a
 * download client's completed folder does not depend on it — see the server-side handler.
 */
export async function fetchDownloadClientSuggestions(
  mediaType: "movie" | "tv",
): Promise<DownloadClientSuggestion[]> {
  const path = `/api/v1/download-clients/suggestions?media_type=${mediaType}`;
  const r = await apiFetch(path);
  await requireOk(path, r, "Could not load download client suggestions");
  return readJson<DownloadClientSuggestion[]>(r);
}
