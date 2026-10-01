/** The media managers and download clients the simulated install has linked. Addresses are this machine only. */
import { shaped } from "../openapi/skeleton.mjs";
import { toWire, SECOND_MS } from "../wire-time.mjs";

export const MACHINE_NAME = "MEDIA-PC";

export const MOVIES_MANAGER_ID = 1;
export const TV_MANAGER_ID = 2;
export const FOUR_K_MANAGER_ID = 3;

export const CONNECTED_DETAIL = "Connected.";

/** How long ago each starter connection last answered, so the answers do not all read the same age. */
const SECONDS_SINCE_LAST_ANSWER = {
  manager: { 1: 22, 2: 41, 3: 9 },
  client: { 1: 33, 2: 54 },
};

/**
 * What a connection reads as when it answered `secondsAgo` seconds before `startedAt`.
 * @param {number} startedAt
 * @param {number} secondsAgo
 */
function answeredAt(startedAt, secondsAgo) {
  return {
    last_test_ok: true,
    last_test_at: toWire(startedAt - secondsAgo * SECOND_MS),
    last_test_detail: CONNECTED_DETAIL,
  };
}

function lane(name) {
  return {
    lane: name,
    enabled: false,
    max_items_per_run: 10,
    retry_delay_minutes: 1440,
    schedule_days: "",
    schedule_enabled: false,
    schedule_end: "23:59",
    schedule_interval_seconds: 3600,
    schedule_start: "00:00",
  };
}

/** What a media manager connection starts with; a new one made from Settings begins here too. */
export const managerDefaults = () => ({
  enabled: true,
  downloaded_scan_enabled: true,
  lanes: [lane("missing"), lane("upgrade")],
});

function manager(startedAt, { id, kind, label, port, nickname = null }) {
  return shaped("MediaManagerConnectionOut", {
    ...managerDefaults(),
    id,
    kind,
    name: `${label} on ${MACHINE_NAME}`,
    nickname,
    base_url: `http://localhost:${port}`,
    api_key_is_saved: true,
    webhook_secret_is_set: true,
    webhook_url_path: `/api/v1/intake/webhook/${kind}-${id}`,
    ...answeredAt(startedAt, SECONDS_SINCE_LAST_ANSWER.manager[id] ?? 30),
  });
}

/** @param {number} startedAt */
export function initialManagers(startedAt) {
  return [
    manager(startedAt, {
      id: MOVIES_MANAGER_ID,
      kind: "radarr",
      label: "Radarr",
      port: 7878,
    }),
    manager(startedAt, {
      id: TV_MANAGER_ID,
      kind: "sonarr",
      label: "Sonarr",
      port: 8989,
    }),
    manager(startedAt, {
      id: FOUR_K_MANAGER_ID,
      kind: "radarr",
      label: "Radarr",
      port: 7879,
      nickname: "4K",
    }),
  ];
}

function downloadClient(startedAt, { id, kind, label, port }) {
  return shaped("DownloadClientConnectionOut", {
    id,
    kind,
    name: `${label} on ${MACHINE_NAME}`,
    nickname: null,
    enabled: true,
    base_url: `http://localhost:${port}`,
    username: null,
    password_is_saved: false,
    api_key_is_saved: kind === "sabnzbd",
    ...answeredAt(startedAt, SECONDS_SINCE_LAST_ANSWER.client[id] ?? 30),
  });
}

/** @param {number} startedAt */
export function initialDownloadClients(startedAt) {
  return [
    downloadClient(startedAt, {
      id: 1,
      kind: "qbittorrent",
      label: "qBittorrent",
      port: 8080,
    }),
    downloadClient(startedAt, {
      id: 2,
      kind: "sabnzbd",
      label: "SABnzbd",
      port: 8085,
    }),
  ];
}

/** The folder each starter manager imports into; a manager made from Settings imports into its kind's usual one. */
const IMPORT_ROOTS = {
  [MOVIES_MANAGER_ID]: "D:\\Media\\Movies",
  [TV_MANAGER_ID]: "D:\\Media\\TV",
  [FOUR_K_MANAGER_ID]: "D:\\Media\\4K Movies",
};

/** @param {{ id: number, kind: string }} connection */
export const importRootOf = (connection) =>
  IMPORT_ROOTS[connection.id] ??
  (connection.kind === "sonarr" ? "D:\\Media\\TV" : "D:\\Media\\Movies");

/** The kind's own name, as operator messages say it: Radarr, Sonarr. @param {string} kind */
export const kindLabel = (kind) =>
  kind === "radarr" ? "Radarr" : kind === "sonarr" ? "Sonarr" : kind;

/** The manager as a message names it: its kind, then its nickname when it has one. @param {{ kind: string, nickname?: string | null }} manager */
export const managerLabel = (manager) =>
  manager.nickname
    ? `${kindLabel(manager.kind)} (${manager.nickname})`
    : kindLabel(manager.kind);

/**
 * What the capabilities endpoint reports for each linked manager.
 * @param {Record<string, any>[]} managers
 */
export function managerCapabilities(managers) {
  return managers.map((m) =>
    shaped("MediaManagerCapabilityOut", {
      connection_id: m.id,
      kind: m.kind,
      label: managerLabel(m),
      name: m.name,
      media_scopes: [m.kind === "radarr" ? "movie" : "tv"],
      reachable: m.last_test_ok === true,
      reports_import_queue: m.last_test_ok === true,
      reports_library_truth: m.last_test_ok === true,
      detail:
        m.last_test_ok === true
          ? ""
          : "The manager did not answer when Weir asked.",
      summary:
        m.last_test_ok === true
          ? "Reports its import queue and what it has in its library."
          : "Weir cannot ask this manager anything while it is not answering.",
      library_roots: [importRootOf(m)],
    }),
  );
}
