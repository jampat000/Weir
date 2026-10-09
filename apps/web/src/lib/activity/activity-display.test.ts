import { describe, expect, it } from "vitest";

import type { ActivityEventItem } from "../api/types";
import { eventDisplay, eventLabel } from "./activity-display";
import {
  REMUX_PASS_COMPLETED_EVENT,
  SKIPPED_REPEAT_EVENT,
} from "./event-types";

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

  it.each(["skipped_guardrail", "source_not_ready", "source_gone"])(
    "never shows a pass that finished nothing (%s) as processed",
    (outcome) => {
      const display = eventDisplay({
        ...passEvent({ outcome, ok: true, reason: "Left alone for a reason." }),
        title: "Skipped Gallery.mkv",
      });

      expect(display.title).toBe("Skipped Gallery.mkv");
      expect(display.summary).toBe("Left alone for a reason.");
      expect(display.meaning).toBe("idle");
      expect(display.chip).not.toBe("File processed");
    },
  );

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
          "alice paused processing until you resume it.",
        ),
      ).title,
    ).toBe("Processing paused");
    expect(
      eventDisplay(
        entry("system.processing_resumed", "alice resumed processing."),
      ).title,
    ).toBe("Processing resumed");
  });
});

describe("eventDisplay for a repeat that was skipped", () => {
  it("shows the server's title and reads as left alone, not as needing attention", () => {
    const display = eventDisplay({
      id: 3,
      created_at: "2026-10-07T03:24:00",
      event_type: SKIPPED_REPEAT_EVENT,
      module: "processing",
      title: "Skipped: already imported (Film.mkv)",
      detail: JSON.stringify({ status: "skipped" }),
    });

    expect(display.title).toBe("Skipped: already imported (Film.mkv)");
    expect(display.meaning).toBe("idle");
  });

  const skipFor = (trigger?: string) =>
    eventDisplay({
      id: 4,
      created_at: "2026-10-07T03:24:00",
      event_type: SKIPPED_REPEAT_EVENT,
      module: "processing",
      title: "Skipped: already done (Film.mkv)",
      detail: JSON.stringify({
        result: "skipped",
        ...(trigger ? { trigger } : {}),
        user_message:
          "Already done: cleaned on 2026-10-07 into D:\\Weir\\Output\\Film\\Film.mkv",
        relative_media_path: "Film/Film.mkv",
        cleaned_at: "2026-10-07T03:20:00.0000000+00:00",
      }),
    });

  it("is listed in full, however long the server's detail is, when the person asked for the file again", () => {
    expect(skipFor("manual").compact).toBe(false);
  });

  it("is routine when a manager's resend or a scan caused it, so a flood of them cannot push real news away", () => {
    expect(skipFor("webhook").compact).toBe(true);
    expect(skipFor("scheduled").compact).toBe(true);
    expect(skipFor().compact).toBe(true);
  });
});

describe("eventLabel", () => {
  it("titles the events added with workflow set-up, repeats and pausing", () => {
    expect(eventLabel("processing.workflow_sync_notice")).toBe(
      "Workflow could not be updated yet",
    );
    expect(eventLabel("processing.file_skipped_repeat")).toBe(
      "Skipped: already done or imported",
    );
    expect(eventLabel("system.processing_paused")).toBe("Processing paused");
    expect(eventLabel("system.processing_resumed")).toBe("Processing resumed");
  });

  it("falls back to the last part of an unknown type, in words", () => {
    expect(eventLabel("processing.something_new")).toBe("something new");
  });
});
