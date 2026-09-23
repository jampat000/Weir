import { describe, expect, it } from "vitest";
import type { ProcessingFile } from "../../lib/processing/files-api";
import {
  agoWords,
  historyGroupOf,
  readableTrack,
  sizesFromRecord,
  tracksFromRecord,
} from "./history-model";

function file(partial: Partial<ProcessingFile>): ProcessingFile {
  return {
    id: 1,
    library_id: 1,
    library_name: "TV",
    relative_path: "Show/Show.S01E01.mkv",
    status: "processed",
    status_reason: "",
    blocked_by_connection: null,
    size_bytes: 1,
    failure_class: null,
    failure_attempts: 0,
    next_retry_at: null,
    output_collision_policy: null,
    output_collision_action: null,
    output_collision_reason: null,
    video_width: null,
    video_height: null,
    video_codec: null,
    audio_track_count: null,
    subtitle_track_count: null,
    duration_seconds: null,
    direct_play: [],
    progress_percent: null,
    progress_message: null,
    progress_eta_seconds: null,
    hold_until: null,
    size_changed_at: null,
    created_at: "2026-09-23T04:00:00",
    updated_at: "2026-09-23T04:00:00",
    last_seen_at: null,
    last_attempt_at: null,
    ...partial,
  };
}

describe("history groups", () => {
  it("puts a file that waits on a person under Needs you, whatever its status", () => {
    expect(
      historyGroupOf(file({ status: "processing_failed", quarantined: true })),
    ).toBe("needs");
    expect(historyGroupOf(file({ status: "blocked_upstream" }))).toBe("needs");
  });

  it("sorts the rest by what is happening to them", () => {
    expect(historyGroupOf(file({ status: "on_hold" }))).toBe("working");
    expect(historyGroupOf(file({ status: "passed_through" }))).toBe("finished");
    expect(historyGroupOf(file({ status: "rejected" }))).toBe("failed");
    expect(historyGroupOf(file({ status: "disabled" }))).toBeNull();
  });
});

describe("readable tracks", () => {
  it("names the language, the layout and the codec, and drops the stream number", () => {
    expect(readableTrack("fre eac3 6 ch (stream 2)")).toBe("French 5.1 E-AC-3");
    expect(readableTrack("ger aac 2 ch")).toBe("German 2.0 AAC");
  });

  it("names a bare language code", () => {
    expect(readableTrack("spa")).toBe("Spanish");
    expect(readableTrack("English")).toBe("English");
  });
});

describe("tracks from a pass record", () => {
  it("lists kept tracks and removed ones with the planner's reason", () => {
    const tracks = tracksFromRecord({
      audio_after: "English 5.1 E-AC-3",
      removed_audio: [
        "fre eac3 6 ch (stream 2): removed (not selected — eng eac3 6 ch (stream 1) kept)",
        "English 2.0 (commentary excluded — remove commentary enabled)",
      ],
      subs_after: "English · Japanese",
      removed_subtitles: ["spa"],
      removed_images: ["mjpeg 600x900"],
    });
    expect(tracks).toEqual([
      { kind: "Audio", what: "English 5.1 E-AC-3", kept: true, why: "" },
      {
        kind: "Audio",
        what: "French 5.1 E-AC-3",
        kept: false,
        why: "Not selected — English 5.1 E-AC-3 kept",
      },
      {
        kind: "Audio",
        what: "English 2.0",
        kept: false,
        why: "Commentary excluded — remove commentary enabled",
      },
      { kind: "Subtitle", what: "English", kept: true, why: "" },
      { kind: "Subtitle", what: "Japanese", kept: true, why: "" },
      { kind: "Subtitle", what: "Spanish", kept: false, why: "" },
      { kind: "Other", what: "mjpeg 600x900", kept: false, why: "" },
    ]);
  });

  it("treats the planner's dash and None as no track", () => {
    expect(tracksFromRecord({ audio_after: "—", subs_after: "None" })).toEqual(
      [],
    );
  });

  it("reports sizes only when both are known", () => {
    expect(
      sizesFromRecord({ source_size_bytes: 100, output_size_bytes: 60 }),
    ).toEqual({
      before: 100,
      after: 60,
      saved: 40,
    });
    expect(sizesFromRecord({ source_size_bytes: 100 })).toBeNull();
  });
});

describe("ago", () => {
  it("reads the server's zone-less times as UTC", () => {
    const now = Date.parse("2026-09-23T04:06:00Z");
    expect(agoWords("2026-09-23T04:00:00", now)).toBe("6 min ago");
  });
});
