import { describe, expect, it } from "vitest";

import { activityFileOfJob, formatPayload } from "./log-job-payload";

describe("formatPayload", () => {
  it("indents JSON so it reads", () => {
    expect(
      formatPayload('{"library_id":1,"relative_media_path":"A/b.mkv"}'),
    ).toBe('{\n  "library_id": 1,\n  "relative_media_path": "A/b.mkv"\n}');
  });

  it("shows text that is not JSON as it is", () => {
    expect(formatPayload("not json")).toBe("not json");
  });

  it("has nothing to show for no payload", () => {
    expect(formatPayload(null)).toBeNull();
    expect(formatPayload("  ")).toBeNull();
  });
});

describe("activityFileOfJob", () => {
  it("links to the file by its name, in its workflow, whenever it was last touched", () => {
    const link = activityFileOfJob({
      payload_json:
        '{"library_id": 3, "relative_media_path": "Movies/Heat (1995)/heat.mkv"}',
    });

    expect(link).toBe("/activity?q=heat.mkv&within=all&library=3");
  });

  it("reads a Windows path by its last part", () => {
    expect(
      activityFileOfJob({
        payload_json:
          '{"relative_media_path": "Shows\\\\Dragnet\\\\s02e08.mkv"}',
      }),
    ).toBe("/activity?q=s02e08.mkv&within=all");
  });

  it("has no link for a job that is not about one file", () => {
    expect(activityFileOfJob({ payload_json: '{"library_id": 3}' })).toBeNull();
    expect(activityFileOfJob({ payload_json: null })).toBeNull();
    expect(activityFileOfJob({ payload_json: "not json" })).toBeNull();
  });
});
