import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { ProcessingFileLogEntry } from "../../lib/processing/files-api";
import { PassRecord } from "./activity-pass";

const NOW = Date.parse("2026-10-03T12:00:00Z");

function pass(detail: Record<string, unknown>): ProcessingFileLogEntry {
  return {
    recorded_at: "2026-10-03T11:00:00Z",
    detail,
    story: [],
  } as unknown as ProcessingFileLogEntry;
}

describe("the tracks of a pass", () => {
  it("draws kept tracks green and removed tracks red, in the counts and on each track", () => {
    render(
      <PassRecord
        now={NOW}
        pass={pass({
          audio_after: "English (eng) AAC 5.1",
          removed_audio: [
            "French (fra) AAC 2.0: removed (not a language you keep)",
          ],
        })}
      />,
    );

    const tracks = screen.getByTestId("activity-tracks");
    expect(screen.getByText("1 kept")).toHaveAttribute("data-status", "done");
    expect(screen.getByText("1 removed")).toHaveAttribute(
      "data-status",
      "broken",
    );
    expect(within(tracks).getByText("Kept")).toHaveAttribute(
      "data-status",
      "done",
    );
    expect(within(tracks).getByText("Removed")).toHaveAttribute(
      "data-status",
      "broken",
    );
  });
});
