import { describe, expect, it } from "vitest";

import { connectionEntry } from "../../../lib/connections/connection-fixtures";
import type { ConnectionEntry } from "../../../lib/connections/connection-model";
import {
  checkedWords,
  connectionNameWords,
  connectionSub,
  connectionSubWords,
  connectionTooltip,
  connectionsLine,
  connectionsLineWords,
  groupConnections,
} from "./connections-card-model";

const NOW = Date.parse("2026-10-02T12:00:00Z");

const manager = (id: number, overrides: Partial<ConnectionEntry> = {}) =>
  connectionEntry({
    key: `media_manager:${id}`,
    id,
    name: `Radarr ${id}`,
    ...overrides,
  });

const client = (id: number, overrides: Partial<ConnectionEntry> = {}) =>
  connectionEntry({
    key: `download_client:${id}`,
    kind: "download_client",
    id,
    name: `qBittorrent ${id}`,
    kindLabel: "qBittorrent",
    address: "http://localhost:8080",
    ...overrides,
  });

describe("the groups", () => {
  it("are the media managers, then the download clients, and leave out one with nothing in it", () => {
    expect(
      groupConnections([client(1), manager(1)]).map((group) => group.title),
    ).toEqual(["Media managers", "Download clients"]);
    expect(groupConnections([client(1)]).map((group) => group.title)).toEqual([
      "Download clients",
    ]);
    expect(groupConnections([])).toEqual([]);
  });

  it("put the worst of a group first, a switched-off connection last, and sort the rest by name", () => {
    const [group] = groupConnections([
      manager(1, { name: "B", state: "ok" }),
      manager(2, { name: "A", state: "ok" }),
      manager(3, { name: "C", state: "off", enabled: false }),
      manager(4, { name: "D", state: "down" }),
      manager(5, { name: "E", state: "slow" }),
      manager(6, { name: "F", state: "untested" }),
    ]);

    expect(group.rows.map((row) => row.name)).toEqual([
      "D",
      "E",
      "F",
      "A",
      "B",
      "C",
    ]);
  });

  it("count how many of those switched on are answering well", () => {
    const [group] = groupConnections([
      manager(1),
      manager(2, { state: "slow" }),
      manager(3, { state: "off", enabled: false }),
    ]);

    expect(group.badge).toBe("1/2 OK");
  });

  it.each([
    ["is green when everything answers", [manager(1)], "ok"],
    [
      "is amber when one is slow",
      [manager(1), manager(2, { state: "slow" })],
      "slow",
    ],
    [
      "is red when one is down, even if another is slow",
      [manager(1, { state: "slow" }), manager(2, { state: "down" })],
      "down",
    ],
    [
      "is neutral when one has not been tested and none has a problem",
      [manager(1), manager(2, { state: "untested" })],
      "untested",
    ],
    [
      "is green when the only one with a problem is switched off",
      [manager(1), manager(2, { state: "down", enabled: false })],
      "ok",
    ],
  ])("badge %s", (_, entries, tone) => {
    expect(groupConnections(entries)[0].tone).toBe(tone);
  });
});

describe("the card's one line", () => {
  it("says how many answer, and how many are slow or down", () => {
    expect(
      connectionsLine([
        manager(1),
        manager(2, { state: "slow" }),
        client(1, { state: "down" }),
        client(2, { state: "off", enabled: false }),
      ]),
    ).toBe("2/3 answering · 1 slow · 1 down");
  });

  it("leaves out what is not so", () => {
    expect(connectionsLine([manager(1), client(1)])).toBe("2/2 answering");
  });

  it("narrows to how many answer, then to nothing, for a box without room", () => {
    expect(
      connectionsLineWords([
        manager(1),
        manager(2, { state: "slow" }),
        client(1, { state: "down" }),
      ]),
    ).toEqual(["2/3 answering · 1 slow · 1 down", "2/3 answering", ""]);
    expect(connectionsLineWords([manager(1)])).toEqual(["1/1 answering", ""]);
  });

  it("counts a connection never tested as not answering yet, and says so", () => {
    expect(
      connectionsLine([manager(1), manager(2, { state: "untested" })]),
    ).toBe("1/2 answering · 1 not tested");
  });

  it("says when nothing is connected or everything is switched off", () => {
    expect(connectionsLine([])).toBe("nothing connected yet");
    expect(
      connectionsLine([manager(1, { enabled: false, state: "off" })]),
    ).toBe("everything is switched off");
  });
});

describe("a row's words", () => {
  it("say what it is and where Weir reaches it", () => {
    expect(connectionSub(manager(1))).toBe("Radarr · http://localhost:7878");
  });

  it("say only what it is when it has no address", () => {
    expect(connectionSub(manager(1, { address: "" }))).toBe("Radarr");
  });

  it("say why a connection is down, or that it does not answer", () => {
    expect(
      connectionSub(
        manager(1, { state: "down", detail: "Weir could not reach Radarr." }),
      ),
    ).toBe("Weir could not reach Radarr.");
    expect(connectionSub(manager(1, { state: "down" }))).toBe("not answering");
  });

  it("say how slow a slow connection is, in seconds", () => {
    expect(connectionSub(manager(1, { state: "slow", answerMs: 2384 }))).toBe(
      "Radarr · slow: 2.4 s",
    );
  });

  it("keep everything in the tooltip: the name, what it is, where, and what its last test said", () => {
    expect(
      connectionTooltip(manager(1, { name: "Radarr 1", detail: "Connected." })),
    ).toBe("Radarr 1: Radarr · http://localhost:7878 — Connected.");
  });

  it("say when it was last checked, or that it is being tested, off, or not yet tested", () => {
    const checked = manager(1, { checkedAt: NOW - 120_000 });

    expect(checkedWords(checked, false, NOW)).toBe("2 min ago");
    expect(checkedWords(checked, true, NOW)).toBe("testing…");
    expect(checkedWords(manager(1, { state: "off" }), false, NOW)).toBe(
      "switched off",
    );
    expect(checkedWords(manager(1), false, NOW)).toBe("not yet");
  });
});

describe("a row's words that narrow with the room", () => {
  it("drop where it runs from the name, then the nickname, never cutting a word", () => {
    expect(
      connectionNameWords(
        manager(1, {
          name: "Radarr on LIVINGROOM-HTPC · 4K",
          baseName: "Radarr on LIVINGROOM-HTPC",
          nickname: "4K",
        }),
      ),
    ).toEqual(["Radarr on LIVINGROOM-HTPC · 4K", "Radarr · 4K", "Radarr"]);
    expect(
      connectionNameWords(
        manager(1, {
          name: "qBittorrent on NAS",
          baseName: "qBittorrent on NAS",
        }),
      ),
    ).toEqual(["qBittorrent on NAS", "qBittorrent"]);
  });

  it("drop the address from the second line, and say a down connection's state alone", () => {
    expect(connectionSubWords(manager(1))).toEqual([
      "Radarr · http://localhost:7878",
      "Radarr",
    ]);
    expect(
      connectionSubWords(
        manager(1, { state: "down", detail: "Weir could not reach Radarr." }),
      ),
    ).toEqual(["Weir could not reach Radarr.", "not answering"]);
    expect(
      connectionSubWords(manager(1, { state: "slow", answerMs: 2384 })),
    ).toEqual(["Radarr · slow: 2.4 s", "slow: 2.4 s"]);
  });
});
