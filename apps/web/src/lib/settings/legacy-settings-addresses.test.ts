import { describe, expect, it } from "vitest";

import { legacySettingsAddress } from "./legacy-settings-addresses";

describe("a former Settings address", () => {
  it.each([
    ["", "/setup/workflows"],
    ["?tab=libraries", "/setup/workflows"],
    ["?tab=rules", "/setup/rules"],
    ["?tab=media-managers", "/setup/connections"],
    ["?tab=performance", "/setup/performance"],
    ["?tab=schedule", "/setup/workflows/schedule"],
    ["?tab=cleanup", "/setup/performance/cleanup"],
    ["?tab=alerts", "/setup/connections/alerts"],
  ])("%s lands on %s", (search, target) => {
    expect(legacySettingsAddress(search)).toBe(target);
  });

  it.each([
    ["audio-subtitles", "/setup/rules"],
    ["running", "/setup/performance"],
    ["processing", "/setup/performance"],
    ["housekeeping", "/setup/performance/cleanup"],
    ["maintenance", "/setup/performance/cleanup"],
    ["schedules", "/setup/workflows/schedule"],
    ["notifications", "/setup/connections/alerts"],
  ])("the older name %s lands on %s", (name, target) => {
    expect(legacySettingsAddress(`?tab=${name}`)).toBe(target);
  });

  it.each([
    ["upgrade", "/system?tab=about"],
    ["support", "/system?tab=about"],
    ["backup", "/system?tab=backups"],
    ["security", "/system?tab=security"],
    ["logs", "/system?tab=logs"],
  ])("the tab %s that moved to System lands on %s", (name, target) => {
    expect(legacySettingsAddress(`?tab=${name}`)).toBe(target);
  });

  it("opens the workflow editor from a link that named a workflow", () => {
    expect(legacySettingsAddress("?tab=libraries&edit=12")).toBe(
      "/setup/workflows?edit=12",
    );
  });

  it("opens the add choice from a link that named a media manager", () => {
    expect(legacySettingsAddress("?tab=libraries&addFrom=6")).toBe(
      "/setup/workflows?addFrom=6",
    );
  });

  it("carries any other part of the address along", () => {
    expect(legacySettingsAddress("?tab=media-managers&library=2")).toBe(
      "/setup/connections?library=2",
    );
  });

  it("ignores case and surrounding spaces in the tab name", () => {
    expect(legacySettingsAddress("?tab=%20Media-Managers%20")).toBe(
      "/setup/connections",
    );
  });

  it("lands on Workflows for a tab it has never heard of", () => {
    expect(legacySettingsAddress("?tab=nonsense")).toBe("/setup/workflows");
  });
});
