import { describe, expect, it } from "vitest";

import type { StatusMeaning } from "../../../../lib/ui/status-meaning";
import type { HealthArea, HealthCheck } from "./health-checks";
import {
  areaTallies,
  emptyHealthWords,
  healthHeadline,
  healthSummary,
  listedChecks,
} from "./health-card-model";

function check(
  id: string,
  area: HealthArea,
  meaning: StatusMeaning,
  checkedAt: number | null = null,
): HealthCheck {
  return {
    id,
    area,
    meaning,
    title: id,
    why: "",
    words: "",
    checkedAt,
    fix: null,
    again: { area, key: null },
    workflowId: null,
  };
}

const CHECKS = [
  check("w1", "workflows", "done"),
  check("w2", "workflows", "attention"),
  check("c1", "connections", "broken"),
  check("c2", "connections", "done"),
  check("t1", "tools", "done"),
  { ...check("t2", "tools", "idle"), fact: true as const },
  check("b1", "backups", "idle"),
];

describe("areaTallies", () => {
  const tallies = areaTallies(CHECKS);

  it("lists every area in the ribbon's order, even one with no checks", () => {
    expect(tallies.map((tally) => tally.key)).toEqual([
      "workflows",
      "connections",
      "tools",
      "storage",
      "backups",
      "weir",
    ]);
    expect(tallies.find((tally) => tally.key === "storage")).toMatchObject({
      ok: 0,
      total: 0,
    });
  });

  it("counts the checks that pass out of the checks there are, leaving notes out", () => {
    expect(tallies.find((tally) => tally.key === "tools")).toMatchObject({
      ok: 1,
      total: 1,
      meaning: "done",
    });
  });

  it("tints an area by its worst check", () => {
    expect(tallies.find((tally) => tally.key === "workflows")?.meaning).toBe(
      "attention",
    );
    expect(tallies.find((tally) => tally.key === "connections")?.meaning).toBe(
      "broken",
    );
  });
});

describe("healthSummary", () => {
  it("counts what passes, what there is and what needs you", () => {
    expect(healthSummary(CHECKS)).toEqual({
      pass: 3,
      total: 6,
      need: 2,
      meaning: "broken",
    });
  });
});

describe("healthHeadline", () => {
  it("reads 'N of M pass' with how many need you", () => {
    expect(healthHeadline(healthSummary(CHECKS), false, null)).toBe(
      "3 of 6 pass · 2 need you",
    );
  });

  it("says one needs you in the singular, and all good when none does", () => {
    expect(
      healthHeadline(
        { pass: 9, total: 10, need: 1, meaning: "attention" },
        false,
        null,
      ),
    ).toBe("9 of 10 pass · 1 needs you");
    expect(
      healthHeadline(
        { pass: 10, total: 10, need: 0, meaning: "done" },
        false,
        null,
      ),
    ).toBe("10 of 10 pass · all good");
  });

  it("uses Weir's own count while a folder check has not answered", () => {
    expect(
      healthHeadline({ pass: 1, total: 2, need: 0, meaning: "done" }, true, {
        passing: 9,
        total: 10,
      }),
    ).toBe("9 of 10 pass · all good");
  });

  it("says nothing before there is any check", () => {
    expect(
      healthHeadline(
        { pass: 0, total: 0, need: 0, meaning: "done" },
        false,
        null,
      ),
    ).toBe("");
  });
});

describe("listedChecks", () => {
  it("lists every check with no area picked, the problems first and the worst of them first", () => {
    expect(listedChecks(CHECKS, null).map((item) => item.id)).toEqual([
      "c1",
      "w2",
      "b1",
      "w1",
      "c2",
      "t1",
      "t2",
    ]);
  });

  it("lists every check of the area picked, problems first", () => {
    expect(listedChecks(CHECKS, "workflows").map((item) => item.id)).toEqual([
      "w2",
      "w1",
    ]);
  });

  it("lists notes after the passing checks of their area", () => {
    expect(listedChecks(CHECKS, "tools").map((item) => item.id)).toEqual([
      "t1",
      "t2",
    ]);
  });
});

describe("emptyHealthWords", () => {
  it("says Weir is still looking when no area is picked and there is nothing to list", () => {
    expect(emptyHealthWords(null)).toBe("Weir is still looking.");
  });

  it("names the area picked when it has nothing to show", () => {
    expect(emptyHealthWords("storage")).toBe("Nothing to show for Storage.");
  });
});
