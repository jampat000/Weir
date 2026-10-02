import { describe, expect, it } from "vitest";
import { normalizeSupportUrl } from "./support";

describe("support helpers", () => {
  it("accepts http and https URLs", () => {
    expect(normalizeSupportUrl("https://github.com/sponsors/example")).toBe(
      "https://github.com/sponsors/example",
    );
    expect(normalizeSupportUrl("http://example.com/support")).toBe(
      "http://example.com/support",
    );
  });

  it("rejects blank and invalid URLs", () => {
    expect(normalizeSupportUrl("")).toBeNull();
    expect(normalizeSupportUrl("   ")).toBeNull();
    expect(normalizeSupportUrl("not-a-url")).toBeNull();
    expect(normalizeSupportUrl("javascript:alert(1)")).toBeNull();
    expect(normalizeSupportUrl("mailto:support@example.com")).toBeNull();
    expect(normalizeSupportUrl("file:///tmp/support-link")).toBeNull();
  });
});
