import { describe, expect, it } from "vitest";

import { appTitle } from "./app-title";

describe("appTitle", () => {
  it("names Weir after the machine it runs on", () => {
    expect(appTitle("RIG")).toBe("Weir · RIG");
    expect(appTitle("  nas-01 ")).toBe("Weir · nas-01");
  });

  it("is just Weir until the machine's name is known", () => {
    expect(appTitle(undefined)).toBe("Weir");
    expect(appTitle("")).toBe("Weir");
    expect(appTitle("   ")).toBe("Weir");
  });
});
