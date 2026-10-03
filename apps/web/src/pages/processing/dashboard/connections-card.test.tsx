import { fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";

import { connectionEntry } from "../../../lib/connections/connection-fixtures";
import type { ConnectionLight } from "../../../lib/connections/connection-lights";
import type { ConnectionEntry } from "../../../lib/connections/connection-model";
import { ConnectionsCard } from "./connections-card";
import type { ConnectionTesting } from "./use-connection-testing";

const NOW = Date.parse("2026-10-02T12:00:00Z");

let fits = Number.MAX_SAFE_INTEGER;
vi.mock("./fit-rows", () => ({
  useFittingRows: () => [{ current: null }, fits],
}));

const radarr = connectionEntry({
  name: "Radarr on MEDIA-PC",
  checkedAt: NOW - 12_000,
  detail: "Connected.",
});
const flaky = connectionEntry({
  key: "media_manager:3",
  id: 3,
  name: "Radarr on MEDIA-PC · 4K",
  address: "http://localhost:7879",
  state: "slow",
  answerMs: 2384,
  checkedAt: NOW - 120_000,
});
const qbittorrent = connectionEntry({
  key: "download_client:1",
  kind: "download_client",
  id: 1,
  name: "qBittorrent on MEDIA-PC",
  kindLabel: "qBittorrent",
  address: "http://localhost:8080",
  state: "down",
  detail: "Weir could not reach qBittorrent.",
  checkedAt: NOW - 30_000,
});
const sabnzbd = connectionEntry({
  key: "download_client:2",
  kind: "download_client",
  id: 2,
  name: "SABnzbd on MEDIA-PC",
  kindLabel: "SABnzbd",
  enabled: false,
  state: "off",
  testable: false,
});

function testingWith(overrides: Partial<ConnectionTesting> = {}) {
  const testing: ConnectionTesting = {
    testing: new Set(),
    test: vi.fn(() => Promise.resolve()),
    testAll: vi.fn(() => Promise.resolve()),
    allBusy: false,
    ...overrides,
  };
  return testing;
}

function renderCard(
  entries: ConnectionEntry[],
  testing = testingWith(),
  lights: ReadonlyMap<string, ConnectionLight> = new Map(),
) {
  render(
    <MemoryRouter>
      <ConnectionsCard
        entries={entries}
        lights={lights}
        testing={testing}
        now={NOW}
      />
    </MemoryRouter>,
  );
  return screen.getByRole("region", { name: "Connections" });
}

describe("the Connections card", () => {
  it("sums up how many answer, and links to where connections are managed", () => {
    const card = renderCard([radarr, flaky, qbittorrent, sabnzbd]);

    expect(card).toHaveTextContent("2/3 answering · 1 slow · 1 down");
    expect(
      within(card).getByRole("link", { name: "Manage: Connections" }),
    ).toHaveAttribute("href", "/setup/connections");
  });

  it("groups media managers and download clients, each with how many of those on are OK", () => {
    const card = renderCard([radarr, flaky, qbittorrent, sabnzbd]);

    const managers = within(card).getByRole("region", {
      name: "Media managers",
    });
    const clients = within(card).getByRole("region", {
      name: "Download clients",
    });
    expect(managers).toHaveTextContent("1/2 OK");
    expect(clients).toHaveTextContent("0/1 OK");
  });

  it("shows each connection's name, what it is and where, and when it was last checked", () => {
    const card = renderCard([radarr]);

    const row = within(card).getByTestId("system-connection");
    expect(row).toHaveTextContent("Radarr on MEDIA-PC");
    expect(row).toHaveTextContent("Radarr · http://localhost:7878");
    expect(row).toHaveTextContent("12s ago");
    expect(row).toHaveAttribute(
      "title",
      "Radarr on MEDIA-PC: Radarr · http://localhost:7878 — Connected.",
    );
  });

  it("puts the worst first in a group, and says in the row why a connection is down or how slow one is", () => {
    const card = renderCard([radarr, flaky]);

    const rows = within(card).getAllByTestId("system-connection");
    expect(rows[0]).toHaveAttribute("data-status", "attention");
    expect(rows[0]).toHaveTextContent("Radarr · slow: 2.4 s");
    expect(rows[1]).toHaveAttribute("data-status", "done");
  });

  it("tints a connection that is down and gives its reason", () => {
    const card = renderCard([qbittorrent]);

    const row = within(card).getByTestId("system-connection");
    expect(row).toHaveAttribute("data-status", "broken");
    expect(row).toHaveTextContent("Weir could not reach qBittorrent.");
  });

  it("dims a switched-off connection and has no Test button for it", () => {
    const card = renderCard([sabnzbd]);

    const row = within(card).getByTestId("system-connection");
    expect(row).toHaveClass("mm-conn--off");
    expect(row).toHaveTextContent("switched off");
    expect(within(row).queryByRole("button")).toBeNull();
  });

  it("tests one connection with its Test button", () => {
    const testing = testingWith();
    const card = renderCard([radarr], testing);

    fireEvent.click(
      within(card).getByRole("button", { name: "Test Radarr on MEDIA-PC" }),
    );

    expect(testing.test).toHaveBeenCalledWith(radarr);
  });

  it("tests every connection with Test all", () => {
    const testing = testingWith();
    const card = renderCard([radarr, qbittorrent], testing);

    fireEvent.click(
      within(card).getByRole("button", { name: "Test all connections" }),
    );

    expect(testing.testAll).toHaveBeenCalledWith([radarr, qbittorrent]);
  });

  it("cannot test all while testing all, or when there is nothing to test", () => {
    const busy = renderCard([radarr], testingWith({ allBusy: true }));
    expect(
      within(busy).getByRole("button", { name: "Test all connections" }),
    ).toBeDisabled();
    expect(busy).toHaveTextContent("Testing…");
  });

  it("has Test all off when no connection can be tested", () => {
    const card = renderCard([sabnzbd]);

    expect(
      within(card).getByRole("button", { name: "Test all connections" }),
    ).toBeDisabled();
  });

  it("lights a row blue and says it is testing while its test runs, with no Test button to press again", () => {
    const card = renderCard(
      [radarr],
      testingWith({ testing: new Set([radarr.key]) }),
    );

    const row = within(card).getByTestId("system-connection");
    expect(row).toHaveAttribute("data-status", "doing");
    expect(row).toHaveTextContent("testing…");
    expect(within(row).queryByRole("button")).toBeNull();
  });

  it.each<[ConnectionLight, string]>([
    ["asking", "doing"],
    ["answered", "done"],
    ["failed", "broken"],
  ])("wears the %s light the stream gives a row", (light, meaning) => {
    const card = renderCard(
      [radarr],
      testingWith(),
      new Map([[radarr.key, light]]),
    );

    expect(within(card).getByTestId("system-connection")).toHaveAttribute(
      "data-status",
      meaning,
    );
  });

  it("says how many connections the card's height left out, not counting the group bars", () => {
    fits = 2;
    const card = renderCard([radarr, flaky, qbittorrent, sabnzbd]);

    // The units are a bar, two rows, a bar and two rows; the first bar and one row fit.
    expect(within(card).getByTestId("system-more")).toHaveTextContent("3 more");
  });

  it("says nothing is left out when every row fits", () => {
    fits = Number.MAX_SAFE_INTEGER;
    const card = renderCard([radarr, flaky, qbittorrent, sabnzbd]);

    expect(within(card).queryByTestId("system-more")).toBeNull();
  });

  it("says nothing is connected when there is nothing to list", () => {
    const card = renderCard([]);

    expect(card).toHaveTextContent("nothing connected yet");
    expect(card).toHaveTextContent("Nothing connected yet.");
  });
});
