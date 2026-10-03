import { describe, expect, it } from "vitest";

import {
  SETUP_AREAS,
  setupAreaForPath,
  setupAreaOfTab,
  setupTabForPath,
  setupTabPath,
  workflowEditorPath,
  workflowFromManagerPath,
} from "./setup-areas";

describe("setup areas", () => {
  it("lists the areas and their tabs in the order the side menu and the tab rows show them", () => {
    expect(
      SETUP_AREAS.map((area) => [
        area.label,
        area.tabs.map((tab) => tab.label),
      ]),
    ).toEqual([
      ["Workflows", ["File paths", "Schedule"]],
      ["Connections", ["Media managers", "Download clients", "Alerts"]],
      ["Rules", ["Profiles", "Playback devices"]],
      ["Performance", ["Speed", "Cleanup", "Weir's timers"]],
    ]);
  });

  it("opens each area on its first tab at the area's own address", () => {
    expect(SETUP_AREAS.map((area) => area.path)).toEqual([
      "/setup/workflows",
      "/setup/connections",
      "/setup/rules",
      "/setup/performance",
    ]);
    for (const area of SETUP_AREAS) {
      expect(area.tabs[0].path).toBe(area.path);
    }
  });

  it("gives every other tab its own address under the area", () => {
    expect(
      SETUP_AREAS.flatMap((area) => area.tabs.slice(1).map((tab) => tab.path)),
    ).toEqual([
      "/setup/workflows/schedule",
      "/setup/connections/download-clients",
      "/setup/connections/alerts",
      "/setup/rules/devices",
      "/setup/performance/cleanup",
      "/setup/performance/timers",
    ]);
  });

  it("reads each tab's address back as that tab", () => {
    for (const area of SETUP_AREAS) {
      for (const tab of area.tabs) {
        expect(setupAreaForPath(tab.path)).toBe(area);
        expect(setupTabForPath(area, tab.path)).toBe(tab);
      }
    }
  });

  it("finds the area and the address of a tab by its id", () => {
    expect(setupAreaOfTab("timers").id).toBe("performance");
    expect(setupTabPath("devices")).toBe("/setup/rules/devices");
  });

  it("does not mistake a longer address for an area that starts the same way", () => {
    expect(setupAreaForPath("/setup")).toBeNull();
    expect(setupAreaForPath("/setup/rules-old")).toBeNull();
    expect(setupAreaForPath("/system")).toBeNull();
  });

  it("shows an area's first tab for an address that names no tab of it", () => {
    const rules = SETUP_AREAS[2];
    expect(setupTabForPath(rules, "/setup/rules/nonsense").id).toBe("profiles");
  });

  it("builds the links that open a workflow's editor and its add choice", () => {
    expect(workflowEditorPath(7)).toBe("/setup/workflows?edit=7");
    expect(workflowFromManagerPath(3)).toBe("/setup/workflows?addFrom=3");
  });
});
