import { describe, expect, it } from "vitest";
import {
  FETCHER_FI_MANUAL_SECTION_BODY,
  FETCHER_FI_MANUAL_SECTION_TITLE,
  FETCHER_FI_SECTION_INTRO_PRIMARY,
  FETCHER_FI_TASKS_SECTION_TITLE,
} from "./user-copy";

describe("fetcher failed-import user-copy (compressed)", () => {
  it("section intro stays short and Fetcher-scoped", () => {
    const t = FETCHER_FI_SECTION_INTRO_PRIMARY.toLowerCase();
    expect(t).toMatch(/queue|failed|sonarr|radarr|blocklist|removefromclient/i);
    expect(t).not.toContain("refiner");
    expect(FETCHER_FI_SECTION_INTRO_PRIMARY.length).toBeLessThan(280);
  });

  it("keeps manual action short", () => {
    expect(FETCHER_FI_MANUAL_SECTION_TITLE.length).toBeLessThan(40);
    expect(FETCHER_FI_MANUAL_SECTION_BODY.length).toBeLessThan(120);
  });

  it("uses calm job history title", () => {
    expect(FETCHER_FI_TASKS_SECTION_TITLE.toLowerCase()).toBe("job history");
  });
});
