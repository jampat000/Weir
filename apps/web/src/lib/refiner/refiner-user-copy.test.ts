import { describe, expect, it } from "vitest";
import {
  REFINER_JOBS_SECTION_TITLE,
  REFINER_MANUAL_QUEUE_SECTION_BODY,
  REFINER_MANUAL_QUEUE_SECTION_TITLE,
  REFINER_PAGE_FRAMING_PRIMARY,
  REFINER_SCHEDULE_MOVIES_HEADING,
  REFINER_SCHEDULE_TV_HEADING,
} from "./refiner-user-copy";

describe("refiner-user-copy (agnostic framing)", () => {
  it("frames Refiner around movies and TV, not *arr plumbing", () => {
    const t = REFINER_PAGE_FRAMING_PRIMARY.toLowerCase();
    expect(t).toContain("movie");
    expect(t).toContain("tv");
    expect(t).not.toContain("radarr");
    expect(t).not.toContain("sonarr");
  });

  it("uses import-cleanup wording instead of internal cleanup-drive labels in panel title and help", () => {
    expect(REFINER_MANUAL_QUEUE_SECTION_TITLE.toLowerCase()).not.toContain("cleanup drive");
    expect(REFINER_MANUAL_QUEUE_SECTION_BODY.toLowerCase()).not.toContain("cleanup drive");
    expect(REFINER_MANUAL_QUEUE_SECTION_TITLE.toLowerCase()).toContain("import");
  });

  it("keeps Radarr/Sonarr only on integration-specific schedule headings", () => {
    expect(REFINER_SCHEDULE_MOVIES_HEADING).toContain("Radarr");
    expect(REFINER_SCHEDULE_TV_HEADING).toContain("Sonarr");
    expect(REFINER_SCHEDULE_MOVIES_HEADING.toLowerCase()).toContain("movies");
    expect(REFINER_SCHEDULE_TV_HEADING.toLowerCase()).toContain("tv");
  });

  it("uses agnostic wording for the jobs table section title", () => {
    expect(REFINER_JOBS_SECTION_TITLE.toLowerCase()).not.toContain("radarr");
    expect(REFINER_JOBS_SECTION_TITLE.toLowerCase()).not.toContain("sonarr");
    expect(REFINER_JOBS_SECTION_TITLE.toLowerCase()).toContain("job");
  });
});
