import { describe, expect, it } from "vitest";

import type { PipelineCard } from "./pipeline-card-types";
import { buildPipelineCards, groupByStage } from "./pipeline-cards";
import {
  NOW,
  aCleanJob,
  aFile,
  aWriting,
  anEndedCard,
  lanesOf,
} from "./pipeline-fixtures";
import { stageOfStep } from "./pipeline-stages";

const stageOf = (cards: PipelineCard[], key: string) =>
  cards.find((card) => card.key === key)?.stage;

const cardsFor = (
  files: Parameters<typeof lanesOf>[0],
  jobs: Parameters<typeof lanesOf>[1] = [],
) => buildPipelineCards(lanesOf(files, jobs), [], "all", NOW);

const written = (card: PipelineCard) =>
  card.details
    .map((line) => {
      const left = line.parts
        .map((part) => (typeof part === "string" ? part : part.bold))
        .join("");
      return line.right ? `${left} ${line.right}` : left;
    })
    .join(" | ");

describe("which station each file is in", () => {
  it("puts a file Weir is still waiting on in Incoming, and a ready one in Queued", () => {
    const cards = cardsFor([
      aFile(1, "on_hold", { status_reason: "Still being copied." }),
      aFile(2, "blocked_upstream", { blocked_by_connection: "Deluno" }),
      aFile(3, "unprocessed"),
      aFile(4, "out_of_schedule", {
        status_reason: "Outside this workflow's hours.",
      }),
    ]);

    expect(cards.map((card) => card.stage)).toEqual([
      "incoming",
      "incoming",
      "queued",
      "queued",
    ]);
  });

  it("puts a pass that is checking or planning in Analysing", () => {
    const cards = cardsFor([
      aFile(1, "processing", { progress_stage: "checking" }),
      aFile(2, "processing", { progress_stage: "planning" }),
    ]);

    expect(cards.map((card) => card.stage)).toEqual(["analysing", "analysing"]);
  });

  it("puts a pass that is writing or verifying in Processing", () => {
    const cards = cardsFor([
      aWriting(1),
      aFile(2, "processing", { progress_stage: "verifying" }),
    ]);

    expect(cards.map((card) => card.stage)).toEqual([
      "processing",
      "processing",
    ]);
  });

  it("puts a pass that is handing back, or on its final checks, in Delivering", () => {
    const cards = cardsFor([
      aFile(1, "processing", { progress_stage: "handing_back" }),
      aFile(2, "processing", { progress_status: "finishing" }),
    ]);

    expect(cards.map((card) => card.stage)).toEqual([
      "delivering",
      "delivering",
    ]);
  });

  it("takes a pass that has named no stage to be checking until it reports a percent, then writing", () => {
    const cards = cardsFor([
      aFile(1, "processing"),
      aFile(2, "processing", { progress_percent: 10 }),
    ]);

    expect(stageOf(cards, "file-1")).toBe("analysing");
    expect(stageOf(cards, "file-2")).toBe("processing");
  });

  it("puts a library clean that is running in Processing and one that is waiting in Queued", () => {
    const cards = cardsFor(
      [],
      [aCleanJob(7, "leased"), aCleanJob(8, "pending")],
    );

    expect(stageOf(cards, "job-7")).toBe("processing");
    expect(stageOf(cards, "job-8")).toBe("queued");
  });

  it("maps every step of a pass to one station", () => {
    expect(stageOfStep("checking")).toBe("analysing");
    expect(stageOfStep("plan")).toBe("analysing");
    expect(stageOfStep("write")).toBe("processing");
    expect(stageOfStep("verify")).toBe("processing");
    expect(stageOfStep("hand-back")).toBe("delivering");
  });
});

describe("the page filter", () => {
  const files = [
    aFile(1, "on_hold", { status_reason: "Still being copied." }),
    aFile(2, "unprocessed"),
    aWriting(3),
  ];
  const lanes = lanesOf(files, [
    aCleanJob(7, "leased"),
    aCleanJob(8, "pending"),
  ]);
  const keys = (filter: "all" | "download" | "library") =>
    buildPipelineCards(lanes, [], filter, NOW).map((card) => card.key);

  it("shows everything for Everything", () => {
    expect(keys("all")).toEqual(
      expect.arrayContaining(["file-1", "file-2", "file-3", "job-7", "job-8"]),
    );
  });

  it("shows only new downloads for New downloads", () => {
    expect(keys("download").sort()).toEqual(["file-1", "file-2", "file-3"]);
  });

  it("shows only library cleans for Library cleaning, and nothing is arriving", () => {
    expect(keys("library").sort()).toEqual(["job-7", "job-8"]);
  });

  it("applies the filter to the cards of ended files too", () => {
    const ended = [
      anEndedCard(4, { kind: "done" }),
      anEndedCard(5, { kind: "done" }, { source: "library" }),
    ];

    const library = buildPipelineCards(lanes, ended, "library", NOW);

    expect(library.map((card) => card.key)).toContain("file-5");
    expect(library.map((card) => card.key)).not.toContain("file-4");
  });
});

describe("what a card says", () => {
  it("shows a writing pass's percent as the bold lead of its status, with the bar at that percent", () => {
    const [card] = cardsFor([aWriting(1, { progress_percent: 42.6 })]);

    expect(card.status).toMatchObject({
      lead: "42%",
      text: "42% writing · 148×",
      pulse: false,
    });
    expect(card.bar).toEqual({ width: 42, waiting: false, moving: true });
  });

  it("keeps what Weir already shows: how far through the file, the reading rate, how long it has run and the source", () => {
    const [card] = cardsFor([aWriting(1)]);

    expect(written(card)).toContain("18:54 of 45:00");
    expect(written(card)).toContain("Reading");
    expect(written(card)).toContain("running 2 min 14 s");
    expect(written(card)).toContain("Download · TV");
    expect(written(card)).toContain(
      "The.Quiet.Harbour.S01E01.1080p.WEB-DL.mkv",
    );
    expect(card.details.find((line) => line.mono)?.parts).toEqual([
      "The.Quiet.Harbour.S01E01.1080p.WEB-DL.mkv",
    ]);
  });

  it("names a library clean for what it is, with no percent to claim", () => {
    const [card] = cardsFor([], [aCleanJob(7, "leased")]);

    expect(card.status.text).toBe("Cleaning");
    expect(card.bar).toEqual({ width: 35, waiting: true, moving: true });
    expect(written(card)).toContain("Library clean · Movies");
  });

  it("never repeats the status line in the detail lines", () => {
    const files = [
      aWriting(1),
      aFile(2, "processing", { progress_stage: "planning" }),
      aFile(3, "unprocessed"),
      aFile(4, "on_hold", { status_reason: "Still being copied." }),
      aFile(5, "processing", { progress_stage: "handing_back" }),
    ];

    for (const card of cardsFor(files)) {
      expect(written(card)).not.toContain(card.status.text);
      if (card.status.lead)
        expect(written(card)).not.toContain(card.status.lead);
    }
  });

  it("numbers the cards waiting in Queued in the order they are in line", () => {
    const cards = cardsFor([aFile(1, "unprocessed"), aFile(2, "unprocessed")]);

    expect(written(cards[0])).toContain("#1 in line");
    expect(written(cards[1])).toContain("#2 in line");
  });

  it("counts an arriving file down to its own wait, with the bar showing how much of the wait has passed", () => {
    const holdUntil = new Date(NOW + 30_000).toISOString();
    const since = new Date(NOW - 30_000).toISOString();
    const [card] = cardsFor([
      aFile(1, "on_hold", {
        status_reason: "Still being written.",
        hold_until: holdUntil,
        size_changed_at: since,
      }),
    ]);

    expect(card.status).toMatchObject({
      text: "Waiting to settle",
      full: "Still being written.",
    });
    expect(written(card)).toContain("ready in 0:30");
    expect(card.bar).toEqual({ width: 50, waiting: false, moving: false });
  });

  it("says what kind of hold an arriving file is on in a few words, whatever sentence the server gave", () => {
    const heldFor = (reason: string) =>
      cardsFor([aFile(1, "on_hold", { status_reason: reason })])[0].status.text;

    expect(heldFor("This file changed too recently. Weir waits 60s.")).toBe(
      "Waiting to settle",
    );
    expect(
      heldFor("Weir could not open this file for reading — it is locked."),
    ).toBe("Can't open it yet");
    expect(heldFor("Something Weir has no short word for.")).toBe("On hold");
    expect(heldFor("")).toBe("Waiting");
  });

  it("says Weir is checking an arriving file once its wait is over", () => {
    const [card] = cardsFor([
      aFile(1, "on_hold", {
        status_reason: "Still being written.",
        hold_until: new Date(NOW - 1000).toISOString(),
      }),
    ]);

    expect(card.status).toMatchObject({ text: "Checking now", pulse: true });
  });
});

describe("cards of files that have just ended", () => {
  const lanes = lanesOf([aWriting(2)]);

  it("shows a delivered file in Delivering, full and green, with the delivered lead", () => {
    const cards = buildPipelineCards(
      lanes,
      [anEndedCard(1, { kind: "done" })],
      "all",
      NOW,
    );
    const ended = cards.find((card) => card.key === "file-1");

    expect(ended).toMatchObject({
      stage: "delivering",
      end: "delivered",
      status: { lead: "✓ Delivered", tone: "ok" },
      bar: { width: 100, moving: false },
    });
  });

  it("shows a file that failed in the station of the step it stopped at, with why", () => {
    const [ended] = buildPipelineCards(
      lanesOf([]),
      [
        anEndedCard(1, {
          kind: "failed",
          at: "write",
          reason: "Writing stopped · original kept",
        }),
      ],
      "all",
      NOW,
    );

    expect(ended).toMatchObject({
      stage: "processing",
      end: "failed",
      status: { text: "Couldn't finish", tone: "bad" },
    });
    expect(written(ended)).toContain("Writing stopped · original kept");
  });

  it("shows a rejected file as a decision, not a failure", () => {
    const [ended] = buildPipelineCards(
      lanesOf([]),
      [
        anEndedCard(1, {
          kind: "rejected",
          at: "plan",
          reason: "Not in a language you keep.",
        }),
      ],
      "all",
      NOW,
    );

    expect(ended).toMatchObject({
      stage: "analysing",
      end: "rejected",
      status: { text: "Rejected", tone: "warn" },
    });
  });

  it("shows only the live card for a file that is back on the board", () => {
    const cards = buildPipelineCards(
      lanes,
      [anEndedCard(2, { kind: "done" })],
      "all",
      NOW,
    );

    expect(cards).toHaveLength(1);
    expect(cards[0].end).toBeNull();
  });

  it("puts ended cards first at their station, so they are never pushed below the rows that show", () => {
    const cards = buildPipelineCards(
      lanesOf([
        aFile(2, "processing", { progress_stage: "handing_back" }),
        aFile(3, "processing", { progress_stage: "handing_back" }),
      ]),
      [anEndedCard(1, { kind: "done" })],
      "all",
      NOW,
    );

    expect(groupByStage(cards).delivering.map((card) => card.key)).toEqual([
      "file-1",
      "file-2",
      "file-3",
    ]);
  });
});
