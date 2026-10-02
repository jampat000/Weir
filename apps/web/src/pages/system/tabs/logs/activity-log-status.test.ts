import { describe, expect, it } from "vitest";

import { eventsStatusWords } from "./activity-log-status";

const base = {
  loaded: 3,
  total: 3,
  filtered: false,
  retentionDays: 90,
  oldestDay: "2 Oct",
};

describe("eventsStatusWords", () => {
  it("counts the events, says the list is live and how far back it goes", () => {
    expect(eventsStatusWords(base)).toBe("3 events · live · back to 2 Oct");
  });

  it("says how much of a longer list is in hand", () => {
    expect(eventsStatusWords({ ...base, loaded: 100, total: 2000 })).toBe(
      "Showing 100 of 2,000 events · live · back to 2 Oct",
    );
  });

  it("says the events match when filters are set", () => {
    expect(
      eventsStatusWords({ ...base, filtered: true, total: 1, loaded: 1 }),
    ).toBe("1 event matching · live · back to 2 Oct");
  });

  it("says events are kept until cleared when there is no limit", () => {
    expect(eventsStatusWords({ ...base, retentionDays: 0 })).toBe(
      "3 events · live · kept until cleared",
    );
  });

  it("leaves out how far back when there is no oldest event", () => {
    expect(eventsStatusWords({ ...base, oldestDay: null })).toBe(
      "3 events · live",
    );
  });
});
