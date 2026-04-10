import { describe, expect, it } from "vitest";
import {
  REFINER_JOBS_SECTION_TITLE,
  REFINER_MANUAL_QUEUE_SECTION_BODY,
  REFINER_MANUAL_QUEUE_SECTION_TITLE,
  REFINER_PAGE_FRAMING_PRIMARY,
  REFINER_PAGE_FRAMING_SCOPE,
  REFINER_SCHEDULE_MOVIES_HEADING,
  REFINER_SCHEDULE_TV_HEADING,
} from "./refiner-user-copy";

describe("refiner-user-copy (language model)", () => {
  it("frames Refiner as movies/TV refinement without Radarr/Sonarr in the primary line", () => {
    const t = REFINER_PAGE_FRAMING_PRIMARY.toLowerCase();
    expect(t).toContain("movie");
    expect(t).toContain("tv");
    expect(t).toContain("helps you refine");
    expect(t).not.toContain("radarr");
    expect(t).not.toContain("sonarr");
  });

  it("separates *arr download-queue failed-import removal from stale-file-on-disk cleanup", () => {
    const s = REFINER_PAGE_FRAMING_SCOPE.toLowerCase();
    expect(s).toContain("download queue");
    expect(s).toContain("failed-import");
    expect(s).toMatch(/stale|disk|orphan/);
    expect(s).toContain("not");
  });

  it("names manual queue action as a failed-import pass, not generic cleanup-drive wording", () => {
    const title = REFINER_MANUAL_QUEUE_SECTION_TITLE.toLowerCase();
    const body = REFINER_MANUAL_QUEUE_SECTION_BODY.toLowerCase();
    expect(title).not.toContain("cleanup drive");
    expect(body).not.toContain("cleanup drive");
    expect(title).toContain("failed-import");
    expect(body).toContain("download queue");
  });

  it("keeps Radarr/Sonarr on integration-specific schedule headings only", () => {
    expect(REFINER_SCHEDULE_MOVIES_HEADING).toContain("Radarr");
    expect(REFINER_SCHEDULE_TV_HEADING).toContain("Sonarr");
    expect(REFINER_SCHEDULE_MOVIES_HEADING.toLowerCase()).toContain("download-queue");
    expect(REFINER_SCHEDULE_TV_HEADING.toLowerCase()).toContain("failed-import");
  });

  it("uses task-based section title without job jargon or *arr names", () => {
    const t = REFINER_JOBS_SECTION_TITLE.toLowerCase();
    expect(t).toContain("task");
    expect(t).not.toContain("job");
    expect(t).not.toContain("radarr");
    expect(t).not.toContain("sonarr");
  });
});
