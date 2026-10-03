import { describe, expect, it } from "vitest";

import { fileNowOf } from "./file-now";
import type { PipelineCard } from "./pipeline-card-types";

function card(overrides: Partial<PipelineCard>): PipelineCard {
  return {
    key: "file-1",
    stage: "processing",
    source: "download",
    title: "Charge (2022)",
    path: "Charge.2022.mkv",
    workflow: "Movies",
    file: null,
    end: null,
    status: { text: "39% · 16 s left", meaning: "doing", pulse: true },
    bar: { width: 39, waiting: false, moving: true },
    details: [],
    fullFacts: ["Speed 479× real time"],
    ...overrides,
  };
}

describe("a file's place on the Pipeline, as its story tells it", () => {
  it("names the station, the status and every fact its card holds", () => {
    expect(fileNowOf(card({}))).toEqual({
      stage: "Processing",
      status: "39% · 16 s left",
      working: true,
      progress: 39,
      facts: ["Speed 479× real time"],
    });
  });

  it("says a file is being worked on while its card's bar is moving, though nothing pulses", () => {
    const now = fileNowOf(
      card({
        status: { text: "39%", meaning: "doing", pulse: false },
        bar: { width: 39, waiting: false, moving: true },
      }),
    );

    expect(now.working).toBe(true);
  });

  it("says a file is not being worked on while it only waits", () => {
    const now = fileNowOf(
      card({
        status: { text: "Waiting its turn", meaning: "todo", pulse: false },
        bar: { width: 0, waiting: true, moving: false },
      }),
    );

    expect(now.working).toBe(false);
  });

  it("gives no progress while the card's bar only says the work is under way", () => {
    const now = fileNowOf(
      card({ bar: { width: 35, waiting: true, moving: true } }),
    );

    expect(now.progress).toBeNull();
  });

  it("gives no progress for a card with no bar", () => {
    expect(fileNowOf(card({ bar: null })).progress).toBeNull();
  });
});
