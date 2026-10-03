import { afterEach, describe, expect, it, vi } from "vitest";

import { readShelfWorkflow, saveShelfWorkflow } from "./shelf-workflow-choice";

afterEach(() => {
  localStorage.clear();
  vi.restoreAllMocks();
});

describe("the shelf's remembered workflow", () => {
  it("is all workflows until one has been chosen", () => {
    expect(readShelfWorkflow()).toBeNull();
  });

  it("comes back as the workflow that was chosen", () => {
    saveShelfWorkflow(3);

    expect(readShelfWorkflow()).toBe(3);
  });

  it("goes back to all workflows when All is chosen", () => {
    saveShelfWorkflow(3);
    saveShelfWorkflow(null);

    expect(readShelfWorkflow()).toBeNull();
  });

  it("is kept under one name, so the choice survives a reload", () => {
    saveShelfWorkflow(2);

    expect(localStorage.getItem("weir.live.shelfWorkflow")).toBe("2");
  });

  it("ignores a stored value that is not a workflow id", () => {
    localStorage.setItem("weir.live.shelfWorkflow", "movies");

    expect(readShelfWorkflow()).toBeNull();
  });

  it("carries on when the browser will not let it read or write", () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new Error("blocked");
    });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("blocked");
    });

    expect(() => saveShelfWorkflow(2)).not.toThrow();
    expect(readShelfWorkflow()).toBeNull();
  });
});
