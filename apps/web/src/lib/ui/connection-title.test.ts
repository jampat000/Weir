import { expect, it } from "vitest";

import { connectionTitle } from "./connection-title";

it("reads a connection with a nickname as its name, then the nickname", () => {
  expect(connectionTitle({ name: "Radarr on nas", nickname: "4K" })).toBe(
    "Radarr on nas · 4K",
  );
});

it("reads a connection with no nickname as its name alone", () => {
  expect(connectionTitle({ name: "Radarr on nas", nickname: null })).toBe(
    "Radarr on nas",
  );
  expect(connectionTitle({ name: "Radarr on nas" })).toBe("Radarr on nas");
});

it("ignores a nickname that is only spaces", () => {
  expect(connectionTitle({ name: "Radarr on nas", nickname: "   " })).toBe(
    "Radarr on nas",
  );
});
