import { describe, expect, it } from "vitest";
import type { FinishedFile } from "../../lib/activity/processing-outcome";
import type { ProcessingFile } from "../../lib/processing/files-api";
import type { ProcessingJobInspectionRow } from "../../lib/processing/jobs-inspection/types";
import {
  LIBRARY_CLEAN_JOB_KIND,
  ago,
  buildLanes,
  fileFacts,
  finishedLine,
  prettyName,
  readRate,
  ringLabel,
  ringState,
  arrivingDeadline,
  runningFor,
  secondsLeft,
  speedWords,
  clock,
  handedBack,
  handedBackSince,
  timeLeft,
} from "./processing-model";

const NOW = Date.parse("2026-09-22T10:00:00Z");

function file(overrides: Partial<ProcessingFile>): ProcessingFile {
  return {
    id: 1,
    library_id: 2,
    library_name: "TV",
    relative_path: "The.Quiet.Harbour.S01E03.1080p.WEB-DL.mkv",
    status: "unprocessed",
    status_reason: "",
    blocked_by_connection: null,
    size_bytes: 2_437_000_000,
    failure_class: null,
    failure_attempts: 0,
    next_retry_at: null,
    output_collision_policy: null,
    output_collision_action: null,
    output_collision_reason: null,
    video_width: 1920,
    video_height: 1080,
    video_codec: "h264",
    audio_track_count: 5,
    subtitle_track_count: 7,
    duration_seconds: 2700,
    direct_play: [],
    progress_percent: null,
    progress_message: null,
    progress_eta_seconds: null,
    hold_until: null,
    size_changed_at: null,
    created_at: "2026-09-22T09:50:00",
    updated_at: "2026-09-22T09:59:00",
    last_seen_at: null,
    last_attempt_at: null,
    ...overrides,
  };
}

function job(
  overrides: Partial<ProcessingJobInspectionRow>,
): ProcessingJobInspectionRow {
  return {
    id: 40,
    dedupe_key: "clean-40",
    job_kind: LIBRARY_CLEAN_JOB_KIND,
    status: "pending",
    attempt_count: 0,
    max_attempts: 3,
    lease_owner: null,
    lease_expires_at: null,
    last_error: null,
    payload_json: JSON.stringify({
      library_id: 1,
      path: "Paper Lanterns (2023)/Paper.Lanterns.2023.1080p.BluRay.mkv",
    }),
    created_at: "2026-09-22T09:40:00",
    updated_at: "2026-09-22T09:40:00",
    ...overrides,
  } as ProcessingJobInspectionRow;
}

const NAMES = new Map([
  [1, "Movies"],
  [2, "TV"],
]);
const AGES = new Map([
  [1, 60],
  [2, 60],
]);

describe("prettyName", () => {
  it("reads an episode as the show and its number", () => {
    expect(
      prettyName(
        "Show Folder/The.Quiet.Harbour.S01E03.1080p.WEB-DL.DDP5.1.H.264-GRP.mkv",
      ),
    ).toBe("The Quiet Harbour S01E03");
  });

  it("reads a film as its title and year", () => {
    expect(prettyName("Paper.Lanterns.2023.1080p.BluRay.x264.mkv")).toBe(
      "Paper Lanterns (2023)",
    );
    expect(prettyName("Paper Lanterns (2023)/Paper Lanterns (2023).mkv")).toBe(
      "Paper Lanterns (2023)",
    );
  });

  it("falls back to the file name in words", () => {
    expect(prettyName("downloads/home_video_final.mkv")).toBe(
      "home video final",
    );
  });
});

describe("fileFacts", () => {
  it("lists what the probe measured and leaves out what it did not", () => {
    expect(fileFacts(file({}))).toBe("1080p · H264 · 2.27 GB");
    expect(fileFacts(file({ video_height: null, video_codec: null }))).toBe(
      "2.27 GB",
    );
  });
});

describe("buildLanes", () => {
  it("puts a timed wait in Arriving, with how long the whole wait is", () => {
    const lanes = buildLanes(
      [
        file({
          status: "on_hold",
          status_reason:
            "This file changed too recently. Weir waits 60s after the last change.",
          size_changed_at: "2026-09-22T09:59:30",
          hold_until: "2026-09-22T10:00:30",
        }),
      ],
      [],
      NAMES,
      AGES,
    );
    expect(lanes.arriving).toHaveLength(1);
    expect(lanes.arriving[0].holdUntil).toBe(
      Date.parse("2026-09-22T10:00:30Z"),
    );
    expect(lanes.arriving[0].holdTotal).toBe(60);
    expect(lanes.arriving[0].note).toBe("This file changed too recently.");
    expect(lanes.arriving[0].upstream).toBe(false);
  });

  it("counts a file on hold after repeated failures as stuck, not arriving", () => {
    const lanes = buildLanes(
      [file({ status: "on_hold", quarantined: true, failure_attempts: 3 })],
      [],
      NAMES,
      AGES,
    );
    expect(lanes.arriving).toHaveLength(0);
    expect(lanes.stuck).toHaveLength(1);
  });

  it("shows a file the media manager is still importing as arriving, with no clock", () => {
    const lanes = buildLanes(
      [file({ status: "blocked_upstream", blocked_by_connection: "Sonarr" })],
      [],
      NAMES,
      AGES,
    );
    expect(lanes.arriving[0]).toMatchObject({
      upstream: true,
      holdUntil: null,
      note: "Sonarr is still importing it.",
    });
  });

  it("queues ready and out-of-hours files in Waiting, in the server's order", () => {
    const lanes = buildLanes(
      [
        file({ id: 1, status: "unprocessed" }),
        file({
          id: 2,
          status: "out_of_schedule",
          status_reason: "Outside the TV library's hours. It starts at 01:00.",
        }),
      ],
      [],
      NAMES,
      AGES,
    );
    expect(lanes.waiting.map((item) => item.key)).toEqual(["file-1", "file-2"]);
    expect(lanes.waiting[1].note).toBe("Outside the TV library's hours.");
  });

  it("splits running files into Working and Handing back by the pass's own status", () => {
    const lanes = buildLanes(
      [
        file({
          id: 1,
          status: "processing",
          progress_status: "processing",
          progress_percent: 46.2,
          progress_eta_seconds: 41,
          progress_speed: "148x",
          progress_removed_audio: [
            "German AC3",
            "French AC3",
            "Spanish AC3",
            "Italian AC3",
          ],
          progress_removed_subtitles: ["German", "French"],
        }),
        file({
          id: 2,
          status: "processing",
          progress_status: "finishing",
          progress_percent: 100,
        }),
      ],
      [],
      NAMES,
      AGES,
    );
    expect(lanes.working).toHaveLength(1);
    expect(lanes.working[0]).toMatchObject({
      percent: 46.2,
      etaSeconds: 41,
      speed: "148x",
      removedAudio: 4,
      removedSubtitles: 2,
      source: "download",
    });
    expect(lanes.handing.map((item) => item.key)).toEqual(["file-2"]);
  });

  it("counts a failed file as stuck and leaves finished files out", () => {
    const lanes = buildLanes(
      [
        file({ id: 1, status: "processing_failed" }),
        file({ id: 2, status: "processed" }),
      ],
      [],
      NAMES,
      AGES,
    );
    expect(lanes.stuck.map((f) => f.id)).toEqual([1]);
    expect(lanes.waiting).toHaveLength(0);
  });

  it("shows library cleans from the job queue: running is working, queued is waiting", () => {
    const lanes = buildLanes(
      [],
      [
        job({ id: 40, status: "leased" }),
        job({ id: 41, status: "pending" }),
        job({ id: 42, status: "completed" }),
        job({
          id: 43,
          status: "pending",
          job_kind: "processing.something_else.v1",
        }),
      ],
      NAMES,
      AGES,
    );
    expect(lanes.working).toHaveLength(1);
    expect(lanes.working[0]).toMatchObject({
      key: "job-40",
      source: "library",
      name: "Paper Lanterns (2023)",
      libraryName: "Movies",
      percent: null,
    });
    expect(lanes.waiting.map((item) => item.key)).toEqual(["job-41"]);
  });

  it("orders Arriving by who starts first", () => {
    const lanes = buildLanes(
      [
        file({ id: 1, status: "on_hold", hold_until: "2026-09-22T10:02:00" }),
        file({ id: 2, status: "blocked_upstream" }),
        file({ id: 3, status: "on_hold", hold_until: "2026-09-22T10:00:20" }),
      ],
      [],
      NAMES,
      AGES,
    );
    expect(lanes.arriving.map((item) => item.key)).toEqual([
      "file-3",
      "file-1",
      "file-2",
    ]);
  });
});

describe("handedBack", () => {
  const finished = (
    at: string,
    kind: FinishedFile["kind"] = "cleaned",
  ): FinishedFile => ({
    id: Math.random(),
    source: "download",
    kind,
    relativePath: "x.mkv",
    libraryId: 2,
    savedBytes: null,
    removedAudio: 0,
    removedSubtitles: 0,
    sentence: null,
    finishedAt: at,
  });
  // 10:02, so the five minutes happening now started at 10:00.
  const at = NOW + 2 * 60_000;

  it("counts files per five minutes on clock boundaries, oldest first, the last being now", () => {
    const result = handedBack(
      [
        finished("2026-09-22T10:01:00"),
        finished("2026-09-22T10:00:00"),
        finished("2026-09-22T09:57:00"),
        finished("2026-09-22T08:05:00"),
        finished("2026-09-22T08:04:59"),
      ],
      at,
    );
    expect(result.buckets).toHaveLength(24);
    expect(new Date(result.buckets[0].from).toISOString()).toBe(
      "2026-09-22T08:05:00.000Z",
    );
    expect(result.buckets.at(-1)?.total).toBe(2);
    expect(result.buckets.at(-2)?.total).toBe(1);
    expect(result.buckets[0].total).toBe(1);
    expect(result.totals.all).toBe(4);
    expect(result.peak).toBe(2);
  });

  it("splits each bucket by how the file turned out, in Just finished's three tones", () => {
    const result = handedBack(
      [
        finished("2026-09-22T10:01:00", "cleaned"),
        finished("2026-09-22T10:01:10", "already"),
        finished("2026-09-22T10:01:20", "passed"),
        finished("2026-09-22T10:01:30", "failed"),
      ],
      at,
    );
    expect(result.buckets.at(-1)).toMatchObject({
      ok: 1,
      same: 1,
      warn: 2,
      total: 4,
    });
    expect(result.totals).toEqual({ ok: 1, same: 1, warn: 2, all: 4 });
  });

  it("keeps a scale of at least one when nothing was handed back", () => {
    const result = handedBack([], at);
    expect(result.totals.all).toBe(0);
    expect(result.peak).toBe(1);
  });

  it("starts the window 23 buckets before the current one, and holds it for five minutes", () => {
    expect(new Date(handedBackSince(at)).toISOString()).toBe(
      "2026-09-22T08:05:00.000Z",
    );
    expect(handedBackSince(NOW + 4 * 60_000 + 59_000)).toBe(
      handedBackSince(at),
    );
  });
});

describe("words and numbers", () => {
  it("says what happened to a finished file", () => {
    const base: FinishedFile = {
      id: 1,
      source: "download",
      kind: "cleaned",
      relativePath: "x.mkv",
      libraryId: 2,
      savedBytes: 318 * 1024 * 1024,
      removedAudio: 4,
      removedSubtitles: 6,
      sentence: null,
      finishedAt: "2026-09-22T09:59:00",
    };
    expect(finishedLine(base)).toBe(
      "Saved 318 MB · removed 4 audio, 6 subtitles",
    );
    expect(finishedLine({ ...base, kind: "already" })).toBe(
      "Already right · handed back as it was",
    );
    expect(
      finishedLine({
        ...base,
        sentence: "Removed 2 audio tracks from Paper Lanterns.",
      }),
    ).toBe("Removed 2 audio tracks from Paper Lanterns.");
  });

  it("counts down, ago and time left in plain words", () => {
    expect(secondsLeft(NOW + 12_400, NOW)).toBe(13);
    expect(secondsLeft(NOW - 5_000, NOW)).toBe(0);
    expect(secondsLeft(null, NOW)).toBeNull();
    expect(ago("2026-09-22T09:59:40", NOW)).toBe("just now");
    expect(ago("2026-09-22T09:56:00", NOW)).toBe("4 min ago");
    expect(ago("2026-09-22T07:50:00", NOW)).toBe("2 h ago");
    expect(timeLeft(41)).toBe("41 s left");
    expect(timeLeft(720)).toBe("12 min left");
    expect(timeLeft(null)).toBe("");
  });
});

describe("the numbers on a file being written", () => {
  it("reads ffmpeg's speed the way a person would, never in scientific notation", () => {
    expect(speedWords("1.26e+03x")).toBe("1,260×");
    expect(speedWords("35.2x")).toBe("35×");
    expect(speedWords("2.46x")).toBe("2.5×");
    expect(speedWords("1.0x")).toBe("1×");
    expect(speedWords("N/A")).toBeNull();
    expect(speedWords(null)).toBeNull();
    expect(speedWords("0x")).toBeNull();
  });

  it("works out how fast the file is being read from how far through it is", () => {
    // 45% of 2 GB in 60 s.
    expect(readRate(2_000_000_000, 45, 60)).toBe(15_000_000);
    expect(readRate(2_000_000_000, null, 60)).toBeNull();
    expect(readRate(2_000_000_000, 45, 0)).toBeNull();
    expect(readRate(null, 45, 60)).toBeNull();
  });

  it("shows a running length and a running time in their own shapes", () => {
    expect(clock(245)).toBe("4:05");
    expect(clock(1120)).toBe("18:40");
    expect(clock(3723)).toBe("1:02:03");
    expect(runningFor(41)).toBe("41 s");
    expect(runningFor(134)).toBe("2 min 14 s");
    expect(runningFor(120)).toBe("2 min");
    expect(runningFor(3900)).toBe("1 h 5 min");
  });

  it("fits a countdown inside its ring", () => {
    expect(ringLabel(58)).toBe("58s");
    expect(ringLabel(582)).toBe("10m");
    expect(ringLabel(2680)).toBe("45m");
    expect(ringLabel(7200)).toBe("2h");
  });
});

describe("an arriving file's ring", () => {
  it("counts down to a known moment, turns only once it has come, and stays still when no time is known", () => {
    expect(ringState(42)).toBe("counting");
    expect(ringState(0)).toBe("checking");
    expect(ringState(null)).toBe("unknown");
  });

  it("counts a wait with no clock of its own down to Weir's next look at the library", () => {
    const at = Date.parse("2026-09-22T10:03:12Z");
    const lanes = buildLanes(
      [
        file({
          id: 1,
          status: "blocked_upstream",
          blocked_by_connection: "Sonarr",
          library_id: 1,
        }),
        file({
          id: 2,
          status: "on_hold",
          status_reason: "Weir could not read this file yet.",
          library_id: 1,
        }),
        file({
          id: 3,
          status: "on_hold",
          hold_until: "2026-09-22T10:00:30Z",
          library_id: 1,
        }),
      ],
      [],
      new Map(),
      new Map(),
      new Map([[1, { at, interval: 300 }]]),
    );

    const byId = (id: number) =>
      lanes.arriving.find((item) => item.file.id === id)!;
    const [upstream, unreadable, timed] = [byId(1), byId(2), byId(3)];
    // The one whose moment comes first leads the lane.
    expect(lanes.arriving[0]).toBe(timed);
    expect(arrivingDeadline(upstream)).toBe(at);
    expect(arrivingDeadline(unreadable)).toBe(at);
    // A file with its own hold counts down to that, not to the next look.
    expect(arrivingDeadline(timed)).toBe(Date.parse("2026-09-22T10:00:30Z"));
    expect(timed.nextLook).toBeNull();
  });
});
