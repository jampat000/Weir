import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";

import { connectionEntry } from "../../../lib/connections/connection-fixtures";
import type { ConnectionLight } from "../../../lib/connections/connection-lights";
import type { ConnectionEntry } from "../../../lib/connections/connection-model";
import { ConnectionLiveRow, whenWords } from "./connection-live-row";

const NOW = Date.parse("2026-10-02T12:00:00Z");

function renderRow(
  overrides: Partial<ConnectionEntry> = {},
  light: ConnectionLight | null = null,
) {
  render(
    <MemoryRouter>
      <ul>
        <ConnectionLiveRow
          entry={connectionEntry(overrides)}
          light={light}
          now={NOW}
          to="/setup/connections"
        />
      </ul>
    </MemoryRouter>,
  );
  return screen.getByTestId("live-connection");
}

describe("what the right of a row says", () => {
  it("is how long ago the connection was last talked to and how long that took", () => {
    expect(
      whenWords(
        connectionEntry({ checkedAt: NOW - 12_000, answerMs: 84 }),
        NOW,
      ),
    ).toBe("12s ago · 84 ms");
  });

  it("leaves the time out when the server has not said how long a call took", () => {
    expect(whenWords(connectionEntry({ checkedAt: NOW - 12_000 }), NOW)).toBe(
      "12s ago",
    );
  });

  it("says a connection that does not answer does not, and when it was found out", () => {
    expect(
      whenWords(
        connectionEntry({ state: "down", checkedAt: NOW - 30_000 }),
        NOW,
      ),
    ).toBe("not answering · 30s ago");
  });

  it("says a connection never tested has not been", () => {
    expect(whenWords(connectionEntry({ state: "untested" }), NOW)).toBe(
      "not tested yet",
    );
  });

  it("says answering when a connection is fine but nothing says when it was last asked", () => {
    expect(whenWords(connectionEntry(), NOW)).toBe("answering");
  });
});

describe("a connection's row", () => {
  it("names the connection, what it is and when it last answered, and links to the managers", () => {
    const row = renderRow({
      name: "Radarr on MEDIA-PC · 4K",
      baseName: "Radarr on MEDIA-PC",
      nickname: "4K",
      checkedAt: NOW - 12_000,
      answerMs: 84,
    });

    expect(row).toHaveTextContent("Radarr on MEDIA-PC · 4K");
    expect(row).toHaveTextContent("manager");
    expect(row).toHaveTextContent("12s ago · 84 ms");
    expect(row.querySelector("a")).toHaveAttribute(
      "href",
      "/setup/connections",
    );
    expect(row.querySelector("a")).toHaveAttribute(
      "title",
      "Radarr on MEDIA-PC · 4K",
    );
  });

  it("keeps the whole name for a screen reader and on hover, whatever words the row has room for", () => {
    const row = renderRow({
      name: "Radarr on MEDIA-PC · 4K",
      baseName: "Radarr on MEDIA-PC",
      nickname: "4K",
    });

    expect(row.querySelector(".sr-only")).toHaveTextContent(
      "Radarr on MEDIA-PC · 4K",
    );
    expect(row.querySelector(".mm-conn__name")).toHaveAttribute(
      "title",
      "Radarr on MEDIA-PC · 4K",
    );
    expect(row.querySelector(".mm-conn__name")).toHaveAttribute(
      "aria-hidden",
      "true",
    );
  });

  it("calls a download client a client", () => {
    expect(
      renderRow({ kind: "download_client", kindLabel: "qBittorrent" }),
    ).toHaveTextContent("client");
  });

  it("says the state in words as well as in the colour of its dot", () => {
    const row = renderRow({ state: "slow", checkedAt: NOW, answerMs: 2400 });

    expect(row).toHaveClass("mm-conn--slow");
    expect(row.querySelector(".mm-conn__when .sr-only")).toHaveTextContent(
      "slow to answer",
    );
  });

  it.each<ConnectionLight>(["asking", "answered", "failed"])(
    "wears the %s light",
    (light) => {
      expect(renderRow({}, light)).toHaveClass(`mm-conn--${light}`);
    },
  );

  it("wears no light when none is on", () => {
    const row = renderRow();

    expect(row.className).not.toMatch(/asking|answered|failed/);
  });

  it("is a unit for the panel's fitting, so a row is never cut through", () => {
    expect(renderRow()).toHaveAttribute("data-fit");
  });
});
