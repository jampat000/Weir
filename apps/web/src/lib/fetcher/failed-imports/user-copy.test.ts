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

describe("fetcher failed-import user-copy (section framing)", () => {
  it("scopes the primary line to this section and names Radarr/Sonarr", () => {
    const t = FETCHER_FI_SECTION_INTRO_PRIMARY.toLowerCase();
    expect(t).toContain("section");
    expect(t).toContain("radarr");
    expect(t).toContain("sonarr");
    expect(t).toContain("mediamop");
  });

  it("separates queue workflow from Refiner stale-on-disk cleanup", () => {
    const s = FETCHER_FI_SECTION_INTRO_SCOPE.toLowerCase();
    expect(s).toMatch(/download.queue|queue/);
    expect(s).toContain("refiner");
    expect(s).toMatch(/stale|disk/);
  });

  it("names manual action as a failed-import pass without cleanup-drive wording", () => {
    const title = FETCHER_FI_MANUAL_SECTION_TITLE.toLowerCase();
    const body = FETCHER_FI_MANUAL_SECTION_BODY.toLowerCase();
    expect(title).not.toContain("cleanup drive");
    expect(body).not.toContain("cleanup drive");
    expect(title).toContain("failed-import");
    expect(body).toContain("download queue");
  });

  it("keeps Radarr/Sonarr on integration-specific schedule headings only", () => {
    expect(FETCHER_FI_SCHEDULE_MOVIES_HEADING).toContain("Radarr");
    expect(FETCHER_FI_SCHEDULE_TV_HEADING).toContain("Sonarr");
    expect(FETCHER_FI_SCHEDULE_MOVIES_HEADING.toLowerCase()).toContain("download-queue");
    expect(FETCHER_FI_SCHEDULE_TV_HEADING.toLowerCase()).toContain("failed-import");
  });

  it("uses a plain recorded-work section title without job jargon or *arr names", () => {
    const t = FETCHER_FI_TASKS_SECTION_TITLE.toLowerCase();
    expect(t).toContain("recorded");
    expect(t).not.toContain("job");
    expect(t).not.toContain("radarr");
    expect(t).not.toContain("sonarr");
  });
});
