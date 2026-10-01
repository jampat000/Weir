/** The media managers and download clients the simulated install has linked. Addresses are this machine only. */
import { shaped } from "../openapi/skeleton.mjs";
import { toWire, MINUTE_MS } from "../wire-time.mjs";

export const MACHINE_NAME = "MEDIA-PC";

const LAST_CHECKED_MINUTES_AGO = 4;

function lastCheck() {
  return {
    last_test_ok: true,
    last_test_at: toWire(Date.now() - LAST_CHECKED_MINUTES_AGO * MINUTE_MS),
    last_test_detail: "Connected.",
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

function manager(id, kind, label, port) {
  return shaped("MediaManagerConnectionOut", {
    ...managerDefaults(),
    id,
    kind,
    name: `${label} on ${MACHINE_NAME}`,
    nickname: null,
    base_url: `http://localhost:${port}`,
    api_key_is_saved: true,
    webhook_secret_is_set: true,
    webhook_url_path: `/api/v1/intake/webhook/${kind}-${id}`,
    ...lastCheck(),
  });
}

export function initialManagers() {
  return [
    manager(1, "radarr", "Radarr", 7878),
    manager(2, "sonarr", "Sonarr", 8989),
  ];
}

function downloadClient(id, kind, label, port) {
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
    ...lastCheck(),
  });
}

export function initialDownloadClients() {
  return [
    downloadClient(1, "qbittorrent", "qBittorrent", 8080),
    downloadClient(2, "sabnzbd", "SABnzbd", 8085),
  ];
}

/** What the capabilities endpoint reports for each linked manager. */
export function managerCapabilities(managers) {
  return managers.map((m) =>
    shaped("MediaManagerCapabilityOut", {
      connection_id: m.id,
      kind: m.kind,
      label: m.kind === "radarr" ? "Radarr" : "Sonarr",
      name: m.name,
      media_scopes: [m.kind === "radarr" ? "movie" : "tv"],
      reachable: true,
      reports_import_queue: true,
      reports_library_truth: true,
      summary: "Reports its import queue and what it has in its library.",
      library_roots: [
        m.kind === "radarr" ? "D:\\Media\\Movies" : "D:\\Media\\TV",
      ],
    }),
  );
}
