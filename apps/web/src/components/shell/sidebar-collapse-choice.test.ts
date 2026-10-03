import { afterEach, describe, expect, it, vi } from "vitest";

import {
  readCollapsedChoice,
  saveCollapsedChoice,
} from "./sidebar-collapse-choice";

afterEach(() => {
  localStorage.clear();
  vi.restoreAllMocks();
});

describe("the menu's folded choice", () => {
  it("is not known until one is made", () => {
    expect(readCollapsedChoice()).toBeNull();
  });

  it("remembers folded and open", () => {
    saveCollapsedChoice(true);
    expect(readCollapsedChoice()).toBe(true);
    saveCollapsedChoice(false);
    expect(readCollapsedChoice()).toBe(false);
  });

  it("ignores what it did not write", () => {
    localStorage.setItem("weir.sidebar.collapsed", "maybe");
    expect(readCollapsedChoice()).toBeNull();
  });

  it("carries on when the browser will not store anything", () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new Error("blocked");
    });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("blocked");
    });
    expect(() => saveCollapsedChoice(true)).not.toThrow();
    expect(readCollapsedChoice()).toBeNull();
  });
});
