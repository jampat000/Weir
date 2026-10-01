import { describe, expect, it } from "vitest";

import {
  SETTINGS_SECTIONS,
  normalizeSettingsSection,
  settingsSection,
  settingsSectionFromSearch,
  settingsSectionPath,
} from "./settings-sections";

describe("settings sections", () => {
  it("lists the sections in the order the side menu shows them", () => {
    expect(SETTINGS_SECTIONS.map((section) => section.label)).toEqual([
      "Workflows",
      "Rules",
      "Media managers",
      "Performance",
      "Schedule",
      "Cleanup",
      "Alerts",
    ]);
  });

  it("opens on Workflows when the address names no section or one that does not exist", () => {
    expect(settingsSectionFromSearch("")).toBe("libraries");
    expect(settingsSectionFromSearch("?tab=nonsense")).toBe("libraries");
  });

  it("reads the names earlier versions used", () => {
    expect(normalizeSettingsSection("audio-subtitles")).toBe("rules");
    expect(normalizeSettingsSection("running")).toBe("performance");
    expect(normalizeSettingsSection("housekeeping")).toBe("cleanup");
    expect(normalizeSettingsSection("schedules")).toBe("schedule");
    expect(normalizeSettingsSection("notifications")).toBe("alerts");
  });

  it("ignores case and surrounding spaces in the section name", () => {
    expect(settingsSectionFromSearch("?tab=%20Media-Managers%20")).toBe(
      "media-managers",
    );
  });

  it("gives every section an address that reads back as the same section", () => {
    for (const section of SETTINGS_SECTIONS) {
      const path = settingsSectionPath(section.id);
      expect(settingsSectionFromSearch(path.split("?")[1] ?? "")).toBe(
        section.id,
      );
    }
  });

  it("keeps the default section's address free of a tab", () => {
    expect(settingsSectionPath("libraries")).toBe("/settings");
    expect(settingsSectionPath("rules")).toBe("/settings?tab=rules");
  });

  it("names the section for the page title", () => {
    expect(settingsSection("media-managers").label).toBe("Media managers");
  });
});
