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
      text: "42% · 10 min left",
      pulse: false,
    });
    expect(card.bar).toEqual({ width: 42, waiting: false, moving: true });
  });

  it("keeps what Weir already shows: how far through the file, the reading rate, how long it has run and the source", () => {
    const [card] = cardsFor([aWriting(1)]);

    expect(written(card)).toContain("18:54 of 45:00");
    expect(written(card)).toContain("Reading");
    expect(written(card)).toContain("Speed 148×");
    expect(written(card)).toContain("Running 2 min 14 s");
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

    expect(card.status).toMatchObject({ text: "Waiting to settle" });
    expect(card.fullFacts).toContain("Still being written.");
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

describe("a working card always has its progress and time left in the status line", () => {
  it("says the percent and the time left, then the same in fewer words, then the percent alone", () => {
    const [card] = cardsFor([aWriting(1, { progress_percent: 81.4 })]);

    expect(card.status).toMatchObject({
      lead: "81%",
      text: "81% · 10 min left",
      fits: ["81% · 10 min", "81%"],
    });
  });

  it("says the percent and that it is writing where the server has no estimate", () => {
    const [card] = cardsFor([
      aWriting(1, { progress_percent: 33, progress_eta_seconds: null }),
    ]);

    expect(card.status).toMatchObject({
      text: "33% writing",
      fits: ["33%"],
    });
  });
});

describe("which of a card's detail lines come first", () => {
  it("puts speed, then what is removed, then the file's resolution and size, then its name, on a writing pass", () => {
    const [card] = cardsFor([
      aWriting(1, {
        progress_removed_audio: ["fra"],
        progress_removed_subtitles: ["fra", "deu"],
      }),
    ]);
    const lines = card.details.map((line) =>
      line.parts
        .map((part) => (typeof part === "string" ? part : part.bold))
        .join(""),
    );

    const at = (start: string) =>
      lines.findIndex((line) => line.startsWith(start));
    expect(at("Speed")).toBe(0);
    expect(at("Removing 1 audio, 2 subtitles")).toBe(1);
    expect(at("1080p")).toBe(2);
    expect(at("The.Quiet.Harbour")).toBeGreaterThan(at("1080p"));
    expect(at("Download · TV")).toBe(lines.length - 1);
  });

  it("puts a waiting file's resolution and size before its place in line, then its name", () => {
    const [card] = cardsFor([aFile(1, "unprocessed")]);

    expect(written(card).indexOf("1080p")).toBeLessThan(
      written(card).indexOf("#1 in line"),
    );
    expect(written(card).indexOf("#1 in line")).toBeLessThan(
      written(card).indexOf("The.Quiet.Harbour"),
    );
  });
});

describe("everything a card knows, for its tooltip and accessible name", () => {
  it("holds all of what main's working card showed", () => {
    const [card] = cardsFor([
      aWriting(1, {
        progress_removed_audio: ["fra"],
        progress_removed_subtitles: ["fra", "deu"],
      }),
    ]);

    expect(card.fullFacts).toEqual([
      "Speed 148× real time",
      expect.stringMatching(/^Reading \d/),
      "Through the file 18:54 of 45:00",
      "Running for 2 min 14 s",
      "Removing 1 audio, 2 subtitles",
      "1080p · H264 · 2.27 GB",
      "Download · TV",
      "The.Quiet.Harbour.S01E01.1080p.WEB-DL.mkv",
    ]);
  });

  it("says where a waiting file stands in line, in words", () => {
    const cards = cardsFor([aFile(1, "unprocessed"), aFile(2, "unprocessed")]);

    expect(cards[0].fullFacts).toContain("1st in line");
    expect(cards[1].fullFacts).toContain("2nd in line");
  });

  it("says a library clean is a library clean", () => {
    const [card] = cardsFor([], [aCleanJob(7, "leased")]);

    expect(card.fullFacts).toContain("Library clean · Movies");
  });

  it("gives an arriving file the whole sentence, with when Weir looks again", () => {
    const lanes = lanesOf(
      [aFile(1, "on_hold", { status_reason: "Still being written." })],
      [],
      new Map([[2, { at: NOW + 83_000, interval: 300 }]]),
    );

    const [card] = buildPipelineCards(lanes, [], "all", NOW);

    expect(card.fullFacts[0]).toBe(
      "Still being written. Weir looks again in 1:23.",
    );
  });

  it("keeps a stopped file's reason, and asks its card not to drop that line", () => {
    const [ended] = buildPipelineCards(
      lanesOf([]),
      [anEndedCard(1, { kind: "failed", at: "write", reason: "Out of space" })],
      "all",
      NOW,
    );

    expect(ended.keep).toBe(1);
    expect(ended.fullFacts[0]).toBe("Out of space");
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
      status: { lead: "✓ Delivered", meaning: "done" },
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
      status: { text: "Couldn't finish", meaning: "broken" },
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
      status: { text: "Rejected", meaning: "attention" },
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

describe("the words a status falls back on in a narrow card", () => {
  const status = (file: ReturnType<typeof aFile>) => cardsFor([file])[0].status;

  it("are a whole briefer word, never the front of the wording cut off", () => {
    expect(status(aFile(1, "unprocessed"))).toMatchObject({
      text: "Waiting its turn",
      fits: ["Waiting"],
    });
    expect(
      status(aFile(2, "processing", { progress_stage: "checking" })),
    ).toMatchObject({ text: "Checking file", fits: ["Checking", "Check"] });
    expect(
      status(aFile(3, "processing", { progress_stage: "handing_back" })),
    ).toMatchObject({ text: "Handing back", fits: ["Handing"] });
  });

  it("are left out of a status that is one word already", () => {
    expect(
      status(aFile(1, "processing", { progress_stage: "planning" })).fits,
    ).toBeUndefined();
  });
});

describe("the briefer wordings of a card's detail lines", () => {
  it("drop the facts from the end, a card narrower than the line losing the file's size first", () => {
    const [card] = cardsFor([aWriting(1)]);
    const facts = card.details.find((line) =>
      line.parts.some(
        (part) => typeof part !== "string" && part.bold.includes("2.27 GB"),
      ),
    );

    expect(facts?.fits).toEqual(["1080p · H264", "1080p", "H264", "2.27 GB"]);
  });

  it("count the tracks being removed where the card cannot name them", () => {
    const [card] = cardsFor([
      aWriting(1, {
        progress_removed_audio: ["fra"],
        progress_removed_subtitles: ["fra", "deu"],
      }),
    ]);
    const removing = card.details.find((line) => line.parts[0] === "Removing ");

    expect(removing?.fits).toEqual(["Removing 3 tracks"]);
  });
});
