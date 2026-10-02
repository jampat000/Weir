/** The settings a person changes and saves: Weir's own, performance, updates, notifications, backups and tools. */
import {
  initialDirectPlayDevices,
  maintenanceFamilies,
} from "../fixtures/settings.mjs";
import { shaped } from "../openapi/skeleton.mjs";
import { addBackup } from "../store.mjs";
import { registerCollection } from "./collection.mjs";
import { registerSingleton } from "./singleton.mjs";

/** Plain-language notes on how the background workers are set up; nothing here can be changed from the web app. */
function runtimeSettings(sim) {
  return shaped("ProcessingRuntimeSettingsOut", {
    in_process_workers_enabled: true,
    in_process_workers_disabled: false,
    in_process_processing_worker_count: sim.engine.slots(),
    worker_mode_summary:
      "Weir does its own processing in the background of this app.",
    processing_media_extensions: [".mkv", ".mp4", ".avi", ".m4v"],
    processing_probe_size_mb: 50,
    processing_analyze_duration_seconds: 10,
    configuration_note: "These are set when Weir starts.",
    visibility_note:
      "Anything that needs a person's attention appears on Processing and in History.",
  });
}

function directPlayDevices(sim) {
  sim.store.directPlay ??= initialDirectPlayDevices();
  return sim.store.directPlay;
}

function saveDirectPlay(sim, body) {
  const selected = new Set(body.selected ?? []);
  const devices = directPlayDevices(sim);
  devices.customised = true;
  for (const device of devices.devices)
    device.selected = selected.has(device.id);
  return devices;
}

/** @param {import("./router.mjs").Router} router */
export function registerSettingsRoutes(router) {
  registerSingleton(router, {
    path: "/api/v1/suite/settings",
    schemaName: "SuiteSettingsOut",
    record: (sim) => sim.store.suite,
  });
  registerSingleton(router, {
    path: "/api/v1/processing/operator-settings",
    schemaName: "ProcessingOperatorSettingsOut",
    record: (sim) => sim.store.operator,
    afterSave: (_saved, sim) => sim.engine.touch(),
  });
  registerSingleton(router, {
    path: "/api/v1/suite/update-settings",
    schemaName: "UpdateSettingsOut",
    record: (sim) => sim.store.updateSettings,
  });
  registerSingleton(router, {
    path: "/api/v1/processing/metadata-provider",
    schemaName: "MetadataProviderOut",
    record: (sim) => sim.store.metadataProvider,
  });
  router.post("/api/v1/processing/metadata-provider/test", ({ sim }) => ({
    status: sim.store.metadataProvider.key_configured
      ? "matched"
      : "not_configured",
    detail: sim.store.metadataProvider.key_configured
      ? "The provider answered."
      : "No provider is set up.",
  }));

  router.get("/api/v1/processing/direct-play/devices", ({ sim }) =>
    directPlayDevices(sim),
  );
  router.put("/api/v1/processing/direct-play/devices", ({ sim, body }) =>
    saveDirectPlay(sim, body),
  );
  router.get("/api/v1/processing/runtime-settings", ({ sim }) =>
    runtimeSettings(sim),
  );

  router.get("/api/v1/processing/maintenance", () => ({
    families: maintenanceFamilies(),
  }));
  router.post("/api/v1/processing/maintenance/run", () => ({
    queued: true,
    job_id: null,
    detail: "Weir will run this in the background.",
  }));

  router.get("/api/v1/suite/notification-channels", ({ sim }) => ({
    items: sim.store.notificationChannels,
    supported_events: [
      "job_completed",
      "job_failed",
      "processing_job_completed",
      "processing_job_failed",
    ],
    supported_providers: ["webhook", "discord"],
  }));
  registerCollection(router, {
    path: "/api/v1/suite/notification-channels",
    schemaName: "NotificationChannelOut",
    idParam: "id",
    records: (sim) => sim.store.notificationChannels,
    readsOne: false,
  });
  router.post("/api/v1/suite/notification-channels/:id/test", () => ({
    ok: true,
    error: null,
  }));

  router.get("/api/v1/suite/configuration-backups", ({ sim }) => ({
    directory: "E:\\Backups\\Weir",
    items: [...sim.store.backups].reverse(),
  }));
  router.post("/api/v1/suite/configuration-backups", ({ sim }) =>
    addBackup(sim.store, sim.now()),
  );
  router.post("/api/v1/suite/apply-update", () => ({
    downloaded: false,
    pending_version: null,
  }));
  router.post("/api/v1/suite/operational-history/reset", ({ sim }) => {
    const activityEvents = sim.engine.activity.all().length;
    sim.engine.activity.retain(() => false);
    sim.engine.touch();
    return {
      status: "reset",
      activity_events_deleted: activityEvents,
      jobs_deleted: 0,
      total_deleted: activityEvents,
    };
  });
}
