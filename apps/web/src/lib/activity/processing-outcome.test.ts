import { describe, expect, it } from "vitest";

import type { ActivityEventItem } from "../api/types";
import { REMUX_PASS_COMPLETED_EVENT } from "./event-types";
import { finishedFileFromEvent } from "./processing-outcome";

function passEvent(detail: Record<string, unknown>): ActivityEventItem {
  return {
    id: 1,
    created_at: "2026-09-29T09:00:00",
    event_type: REMUX_PASS_COMPLETED_EVENT,
    module: "processing",
    title: "",
    library_id: 1,
    relative_path: "Film (2024)/film.mkv",
    detail: JSON.stringify({
      relative_media_path: "Film (2024)/film.mkv",
      ...detail,
    }),
  };
}

const REJECTION_REASON =
  "Rejected: none of its audio tracks are in English, and your rules keep only English audio, so there would be nothing to keep.";

describe("finishedFileFromEvent for a pass that ended in a rejection", () => {
  it("reads a rules rejection as rejected, with the reason and what became of the file", () => {
    const finished = finishedFileFromEvent(
      passEvent({
        outcome: "failed_before_execution",
        ok: false,
        rejected_without_manager: true,
        reason: REJECTION_REASON,
        rejected_cleanup_detail: "The file was left where it is.",
      }),
    );

    expect(finished?.kind).toBe("rejected");
    expect(finished?.sentence).toBe(
      `${REJECTION_REASON} The file was left where it is.`,
    );
  });

  it("still says it was rejected when the pass recorded no reason", () => {
    const finished = finishedFileFromEvent(
      passEvent({
        outcome: "failed_before_execution",
        ok: false,
        rejected_without_manager: true,
      }),
    );

    expect(finished?.kind).toBe("rejected");
    expect(finished?.sentence).toBe("Rejected");
  });

  it("keeps a real failure as failed, with no sentence of its own", () => {
    const finished = finishedFileFromEvent(
      passEvent({ outcome: "failed_during_execution", ok: false }),
    );

    expect(finished?.kind).toBe("failed");
    expect(finished?.sentence).toBeNull();
  });

  it("keeps a failure while a media manager is asked for a replacement as failed", () => {
    const finished = finishedFileFromEvent(
      passEvent({
        outcome: "failed_before_execution",
        ok: false,
        reject_queued: true,
      }),
    );

    expect(finished?.kind).toBe("failed");
  });
});

describe("finishedFileFromEvent for a pass that finished nothing", () => {
  it.each([
    [
      "a file under the workflow's minimum size",
      {
        outcome: "skipped_guardrail",
        guardrail: "minimum_input_file_size",
        skip_kind: "below_minimum_size",
      },
    ],
    [
      "a file a safety check skipped for its age",
      { outcome: "skipped_guardrail", guardrail: "minimum_file_age" },
    ],
    ["a file that was not ready yet", { outcome: "source_not_ready" }],
    ["a file that was gone", { outcome: "source_gone" }],
  ])(
    "is no finished file for %s: nothing was written or checked",
    (_, detail) => {
      expect(
        finishedFileFromEvent(passEvent({ ok: true, ...detail })),
      ).toBeNull();
    },
  );

  it("is cleaned only when the pass wrote an output, and already clean when it checked that none was needed", () => {
    expect(
      finishedFileFromEvent(
        passEvent({ ok: true, outcome: "live_output_written" }),
      )?.kind,
    ).toBe("cleaned");
    expect(
      finishedFileFromEvent(
        passEvent({ ok: true, outcome: "live_skipped_not_required" }),
      )?.kind,
    ).toBe("already");
  });
});

describe("finishedFileFromEvent and the poster", () => {
  it("carries the poster address the entry names", () => {
    const entry = {
      ...passEvent({ outcome: "live_output_written", ok: true }),
      poster_url: "/api/v1/artwork/posters/movie-detour-1945",
    };

    expect(finishedFileFromEvent(entry)?.posterUrl).toBe(
      "/api/v1/artwork/posters/movie-detour-1945",
    );
  });

  it("has none when the entry names none", () => {
    const finished = finishedFileFromEvent(
      passEvent({ outcome: "live_output_written", ok: true }),
    );

    expect(finished?.posterUrl).toBeNull();
  });
});
