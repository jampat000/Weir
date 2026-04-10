import { describe, expect, it } from "vitest";
import {
  manualEnqueueRadarrCleanupDrivePath,
  manualEnqueueSonarrCleanupDrivePath,
} from "./refiner-manual-cleanup-enqueue-api";

describe("manual cleanup-drive enqueue paths", () => {
  it("uses distinct Radarr and Sonarr POST URLs under /api/v1/refiner", () => {
    expect(manualEnqueueRadarrCleanupDrivePath()).toBe("/api/v1/refiner/cleanup-drive/radarr/enqueue");
    expect(manualEnqueueSonarrCleanupDrivePath()).toBe("/api/v1/refiner/cleanup-drive/sonarr/enqueue");
  });
});
