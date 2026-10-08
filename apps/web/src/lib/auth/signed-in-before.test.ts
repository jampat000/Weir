import { beforeEach, describe, expect, it } from "vitest";
import { clearSignedIn, markSignedIn, wasSignedIn } from "./signed-in-before";

describe("wasSignedIn", () => {
  beforeEach(() => {
    clearSignedIn();
  });

  it("is false for a browser that never signed in", () => {
    expect(wasSignedIn()).toBe(false);
  });

  it("is true once the browser has been signed in", () => {
    markSignedIn();
    expect(wasSignedIn()).toBe(true);
  });

  it("is false again after signing out on purpose", () => {
    markSignedIn();
    clearSignedIn();
    expect(wasSignedIn()).toBe(false);
  });
});
