import { sendJson } from "../api/send-json";
import { apiFetch, readJson, requireOk } from "../api/client";
import type { RequestBody, Schema } from "../api/types";

/**
 * The five bare download clients Weir can read a watched-folder suggestion from when there is no
 * Sonarr/Radarr/Deluno to ask instead (#768). This connection is outbound only: Weir reads the
 * client's own configuration to suggest a folder, never controls it and never applies anything
 * automatically.
 */
export type DownloadClientKind = Schema<"DownloadClientConnectionOut">["kind"];

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

/** Secrets are reported only as saved or not, never their value. */
export type DownloadClientConnection = Schema<"DownloadClientConnectionOut">;

export type DownloadClientConnectionCreate =
  RequestBody<"DownloadClientConnectionCreateIn">;
/** Leave a secret out to keep the saved one; send "" to clear it. */
export type DownloadClientConnectionUpdate =
  RequestBody<"DownloadClientConnectionUpdateIn">;
export type DownloadClientConnectionTest =
  Schema<"DownloadClientConnectionTestOut">;
export type DownloadClientCategoryFolder =
  Schema<"DownloadClientCategoryFolderOut">;
export type DownloadClientSuggestionLine = Schema<"ManagerSetupLineOut">;
export type DownloadClientSuggestion = Schema<"DownloadClientSuggestionOut">;

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
