import { describe, expect, it } from "vitest";
import {
  FETCHER_FI_MANUAL_SECTION_BODY,
  FETCHER_FI_MANUAL_SECTION_TITLE,
  FETCHER_FI_SCHEDULE_MOVIES_HEADING,
  FETCHER_FI_SCHEDULE_TV_HEADING,
  FETCHER_FI_SECTION_INTRO_PRIMARY,
  FETCHER_FI_SECTION_INTRO_SCOPE,
  FETCHER_FI_TASKS_SECTION_TITLE,
} from "./user-copy";

describe("fetcher failed-import user-copy (product voice)", () => {
  it("names Radarr and Sonarr without an essay on other modules", () => {
    const t = FETCHER_FI_SECTION_INTRO_PRIMARY.toLowerCase();
    expect(t).toContain("radarr");
    expect(t).toContain("sonarr");
    expect(t).not.toContain("refiner");
  });

  it("describes failed imports in operator language", () => {
    const s = FETCHER_FI_SECTION_INTRO_SCOPE.toLowerCase();
    expect(s).toMatch(/download|queue|list/);
    expect(s).toContain("media");
    expect(s).not.toContain("refiner");
  });

  it("keeps manual action plain", () => {
    const title = FETCHER_FI_MANUAL_SECTION_TITLE.toLowerCase();
    const body = FETCHER_FI_MANUAL_SECTION_BODY.toLowerCase();
    expect(title).not.toContain("cleanup drive");
    expect(body).not.toContain("worker");
    expect(body).not.toContain("in-process");
  });

  it("keeps Radarr/Sonarr on schedule headings", () => {
    expect(FETCHER_FI_SCHEDULE_MOVIES_HEADING).toContain("Radarr");
    expect(FETCHER_FI_SCHEDULE_TV_HEADING).toContain("Sonarr");
  });

  it("uses a non-internal section title for the table", () => {
    const t = FETCHER_FI_TASKS_SECTION_TITLE.toLowerCase();
    expect(t).toBe("history");
    expect(t).not.toContain("job");
  });
});
