import { describe, expect, it } from "vitest";

import type { ActivityEventItem } from "../api/types";
import { eventDisplay } from "./activity-display";
import { REMUX_PASS_COMPLETED_EVENT } from "./event-types";

function passEvent(detail: Record<string, unknown>): ActivityEventItem {
  return {
    id: 1,
    created_at: "2026-09-29T09:00:00",
    event_type: REMUX_PASS_COMPLETED_EVENT,
    module: "processing",
    title: "",
    detail: JSON.stringify({
      relative_media_path: "Film (2024)/film.mkv",
      outcome: "failed_before_execution",
      ok: false,
      ...detail,
    }),
  };
}

describe("eventDisplay for a finished pass", () => {
  it("shows a rules rejection as rejected, not as a failure", () => {
    const display = eventDisplay(
      passEvent({
        rejected_without_manager: true,
        reason: "Rejected: it has no audio tracks.",
      }),
    );

    expect(display.title).toBe("Rejected film.mkv");
    expect(display.summary).toBe("Rejected: it has no audio tracks.");
    expect(display.chip).toBe("Rejected");
    expect(display.meaning).toBe("attention");
  });

  it("shows a real failure as one", () => {
    const display = eventDisplay(passEvent({}));

    expect(display.title).toBe("film.mkv could not be processed");
    expect(display.chip).toBe("Processing failed");
    expect(display.meaning).toBe("broken");
  });
});

describe("eventDisplay for the pause", () => {
  it("names a pause and a resume, and keeps who and until when in the detail", () => {
    const entry = (event_type: string, detail: string): ActivityEventItem => ({
      id: 2,
      created_at: "2026-10-07T03:24:00",
      event_type,
      module: "system",
      title: "",
      detail,
    });

    expect(
      eventDisplay(
        entry(
          "system.processing_paused",
          "Processing was paused until you resume by alice.",
        ),
      ).title,
    ).toBe("Processing paused");
    expect(
      eventDisplay(
        entry("system.processing_resumed", "Processing was resumed by alice."),
      ).title,
    ).toBe("Processing resumed");
  });
});
