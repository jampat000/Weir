import { describe, expect, it } from "vitest";
import {
  FETCHER_FI_MANUAL_SECTION_BODY,
  FETCHER_FI_MANUAL_SECTION_TITLE,
  FETCHER_FI_SCHEDULE_MOVIES_HEADING,
  FETCHER_FI_SCHEDULE_TV_HEADING,
  FETCHER_FI_SECTION_INTRO_PRIMARY,
  FETCHER_FI_TASKS_SECTION_TITLE,
} from "./user-copy";

describe("fetcher failed-import user-copy (compressed)", () => {
  it("section intro is one line with Radarr/Sonarr, no cross-module essay", () => {
    const t = FETCHER_FI_SECTION_INTRO_PRIMARY.toLowerCase();
    expect(t).toContain("radarr");
    expect(t).toContain("sonarr");
    expect(t).toContain("failed import");
    expect(t).not.toContain("refiner");
    expect(FETCHER_FI_SECTION_INTRO_PRIMARY.length).toBeLessThan(200);
  });

  it("keeps manual action short", () => {
    expect(FETCHER_FI_MANUAL_SECTION_TITLE.length).toBeLessThan(40);
    expect(FETCHER_FI_MANUAL_SECTION_BODY.length).toBeLessThan(120);
  });

  it("keeps Radarr/Sonarr on schedule headings", () => {
    expect(FETCHER_FI_SCHEDULE_MOVIES_HEADING).toContain("Radarr");
    expect(FETCHER_FI_SCHEDULE_TV_HEADING).toContain("Sonarr");
  });

  it("uses plain history title", () => {
    expect(FETCHER_FI_TASKS_SECTION_TITLE.toLowerCase()).toBe("history");
  });
});
