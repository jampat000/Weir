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
