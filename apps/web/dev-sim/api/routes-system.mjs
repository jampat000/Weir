/** Readiness, the tools Weir uses, logs, metrics and the folder browser: what the System screens read. */
import {
  APP_VERSION,
  mediaTools,
  readiness,
  updateStatus,
  WEIR_UPTIME_AT_START_MS,
} from "../fixtures/settings.mjs";
import { shaped } from "../openapi/skeleton.mjs";
import { toWire, SECOND_MS } from "../wire-time.mjs";
import { activityLogLines } from "./log-lines.mjs";
import { download, Reply } from "./reply.mjs";

/** The folders the picker can open, as parent path to child names. Drives are the empty path. */
const FOLDER_TREE = {
  "": ["D:\\", "E:\\"],
  "D:\\": ["Downloads", "Media", "Weir"],
  "D:\\Downloads": ["Movies", "TV"],
  "D:\\Media": ["Movies", "TV"],
  "D:\\Weir": ["hand-back", "work"],
  "D:\\Weir\\hand-back": ["Movies", "TV"],
  "E:\\": ["Backups", "Weir"],
  "E:\\Weir": ["work"],
};

const isDriveRoot = (path) => /^[A-Za-z]:\\?$/.test(path);
const normalised = (path) =>
  isDriveRoot(path) ? `${path[0].toUpperCase()}:\\` : path.replace(/\\+$/, "");
const childPath = (parent, name) =>
  parent === ""
    ? name
    : isDriveRoot(parent)
      ? `${parent}${name}`
      : `${parent}\\${name}`;

function parentOf(path) {
  if (path === "") return null;
  if (isDriveRoot(path)) return "";
  const parent = path.slice(0, path.lastIndexOf("\\"));
  return isDriveRoot(`${parent}\\`) ? `${parent}\\` : parent;
}

function browse(requested) {
  const path = normalised(requested);
  return {
    current_path: path === "" ? null : path,
    parent_path: parentOf(path),
    entries: (FOLDER_TREE[path] ?? []).map((name) => ({
      kind: "directory",
      name,
      path: childPath(path, name),
      description: null,
    })),
  };
}

function metrics(sim) {
  const totalRequests = 1840 + Math.round((sim.now() - sim.startedAt) / 250);
  return shaped("SuiteMetricsOut", {
    total_requests: totalRequests,
    average_response_ms: 6.4,
    uptime_seconds:
      (WEIR_UPTIME_AT_START_MS + (sim.now() - sim.startedAt)) / SECOND_MS,
    error_log_count: 0,
    status_counts: { 200: totalRequests - 14, 204: 9, 304: 5 },
    busiest_routes: [
      {
        route: "/api/v1/processing/files",
        request_count: Math.round(totalRequests * 0.31),
        average_response_ms: 8.1,
      },
      {
        route: "/api/v1/activity/recent",
        request_count: Math.round(totalRequests * 0.22),
        average_response_ms: 5.6,
      },
      {
        route: "/api/v1/processing/jobs/inspection",
        request_count: Math.round(totalRequests * 0.18),
        average_response_ms: 4.2,
      },
    ],
  });
}

/** What the Windows package says when the server only accepts connections from this PC, as the server words it. */
const NETWORK_ACCESS = shaped("SuiteNetworkAccessOut", {
  state: "this_pc_only",
  summary:
    "Only this PC can reach Weir. To let other devices on your network in, use the Weir tray icon → Allow other devices on your network.",
});

/** @param {import("./router.mjs").Router} router */
export function registerSystemRoutes(router) {
  router.get("/ready", () => ({ ready: true, status: "ready" }));
  router.get("/health", () => ({
    status: "ok",
    dependencies: { database: "ok", ffmpeg: "ok" },
  }));
  router.get("/api/v1/system/readiness", () => readiness());
  router.get("/api/v1/system/media-tools", () => mediaTools());
  router.get("/api/v1/system/directories", ({ query }) =>
    browse(query.get("path") ?? ""),
  );
  router.get("/api/v1/processing/hardware", () =>
    shaped("ProcessingHardwareOut", {
      detected: false,
      detail:
        "No hardware encoder was found. Weir copies video without re-encoding, so it does not need one.",
    }),
  );
  router.get("/api/v1/suite/update-status", () => updateStatus());
  router.get("/api/v1/suite/update-state", () => ({
    downloaded: false,
    pending_version: null,
  }));
  router.get("/api/v1/suite/network-access", () => NETWORK_ACCESS);
  router.get("/api/v1/suite/metrics", ({ sim }) => metrics(sim));
  router.get("/api/v1/suite/security-overview", () =>
    shaped("SuiteSecurityOverviewOut", {
      session_signing_configured: true,
      allowed_browser_origins_count: 0,
      sign_in_attempt_limit: 8,
      sign_in_attempt_window_plain: "15 minutes",
      first_time_setup_attempt_limit: 5,
      first_time_setup_attempt_window_plain: "15 minutes",
      sign_in_cookie_https_mode: "auto",
      sign_in_cookie_https_plain:
        "Sent over HTTPS only when Weir is reached over HTTPS.",
      sign_in_cookie_same_site: "lax",
      standard_session_idle_timeout_plain: "1 day",
      standard_session_absolute_timeout_plain: "7 days",
      trusted_session_idle_timeout_plain: "7 days",
      trusted_session_absolute_timeout_plain: "30 days",
      restart_required_note:
        "Changes to these settings need a restart of Weir.",
    }),
  );
  router.get("/api/v1/suite/logs", ({ sim, query }) =>
    activityLogLines(sim, query),
  );
  router.get("/api/v1/suite/logs/download", ({ sim }) =>
    download(
      activityLogLines(sim, new URLSearchParams())
        .items.map(
          (line) =>
            `${line.timestamp} ${line.level} ${line.logger} ${line.message}`,
        )
        .join("\n"),
      "weir-logs.txt",
    ),
  );
  router.get("/api/v1/suite/operational-history/preview", ({ sim }) => ({
    status: "preview",
    activity_events_deleted: sim.engine.activity.all().length,
    jobs_deleted: sim.engine.jobs.all().length,
    total_deleted:
      sim.engine.activity.all().length + sim.engine.jobs.all().length,
  }));
  router.get("/api/v1/suite/configuration-bundle", ({ sim }) => ({
    version: APP_VERSION,
    exported_at: toWire(sim.now()),
    libraries: sim.store.libraries.length,
  }));
  router.get(
    "/api/v1/suite/configuration-backups/:id/download",
    ({ params }) =>
      new Reply(200, "{}", {
        contentType: "application/zip",
        filename: `weir-config-${params.id}.zip`,
      }),
  );
}
