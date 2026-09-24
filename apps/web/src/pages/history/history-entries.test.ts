import { describe, expect, it } from "vitest";
import type { ProcessingFile } from "../../lib/processing/files-api";
import type { LibraryClean } from "../../lib/processing/library-cleans-api";
import {
  cleanEntry,
  downloadEntry,
  entryGroup,
  historyEntries,
  historyGroupOf,
  inGroup,
  retryableFailures,
} from "./history-entries";

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
    created_at: "2026-08-19T04:00:00",
    updated_at: "2026-08-19T04:00:00",
    last_seen_at: null,
    last_attempt_at: null,
    ...partial,
  };
}

function clean(partial: Partial<LibraryClean>): LibraryClean {
  return {
    kind: "library_clean",
    id: 1,
    library_id: 2,
    library_name: "Films",
    relative_path: "Heat (1995)/Heat.mkv",
    outcome: "cleaned",
    detail: "Cleaned Heat.mkv: removed 1 audio track.",
    trigger: null,
    recorded_at: "2026-08-19T04:00:00",
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

  it("moves on_hold into Needs you rather than In progress", () => {
    expect(historyGroupOf(file({ status: "on_hold" }))).toBe("needs");
  });

  it("gives a skip its own neutral group rather than counting it as failed", () => {
    expect(historyGroupOf(file({ status: "skipped" }))).toBe("skipped");
  });

  it("sorts the rest by what is happening to them", () => {
    expect(historyGroupOf(file({ status: "unprocessed" }))).toBe("working");
    expect(historyGroupOf(file({ status: "passed_through" }))).toBe("finished");
    expect(historyGroupOf(file({ status: "rejected" }))).toBe("failed");
    expect(historyGroupOf(file({ status: "disabled" }))).toBeNull();
  });
});

describe("a library clean's group", () => {
  it("follows its outcome: cleaned finishes, skipped is neutral, failed is failed", () => {
    expect(entryGroup(cleanEntry(clean({ outcome: "cleaned" })))).toBe(
      "finished",
    );
    expect(entryGroup(cleanEntry(clean({ outcome: "skipped" })))).toBe(
      "skipped",
    );
    expect(entryGroup(cleanEntry(clean({ outcome: "failed" })))).toBe("failed");
  });
});

describe("inGroup", () => {
  it("puts every entry in All, whatever its own group is", () => {
    expect(inGroup(downloadEntry(file({ status: "disabled" })), "all")).toBe(
      true,
    );
  });
});

describe("historyEntries", () => {
  it("lists downloads and cleans together, newest change first", () => {
    const entries = historyEntries(
      [file({ id: 1, updated_at: "2026-08-19T03:00:00" })],
      [clean({ id: 1, recorded_at: "2026-08-19T05:00:00" })],
    );
    expect(entries.map((entry) => entry.kind)).toEqual([
      "library_clean",
      "download",
    ]);
  });
});

describe("retryableFailures", () => {
  it("keeps only the failed downloads, leaving out a failed clean or any other status", () => {
    const entries = historyEntries(
      [
        file({ id: 1, status: "processing_failed" }),
        file({ id: 2, status: "skipped" }),
      ],
      [clean({ id: 1, outcome: "failed" })],
    );
    expect(retryableFailures(entries).map((f) => f.id)).toEqual([1]);
  });
});
