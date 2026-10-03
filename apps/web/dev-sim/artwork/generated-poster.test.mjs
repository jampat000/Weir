// @vitest-environment node
import { describe, expect, it } from "vitest";

import { generatedPoster } from "./generated-poster.mjs";

describe("a drawn poster", () => {
  it("breaks a long title over several lines and shows its year", () => {
    const { body } = generatedPoster({
      title: "The Cabinet of Dr. Caligari",
      year: 1920,
    });

    expect(body.match(/<text /g)?.length).toBeGreaterThan(2);
    expect(body).toContain("1920");
  });

  it("escapes the characters an image cannot carry in a title", () => {
    const { body } = generatedPoster({ title: "Tom & <Jerry>", year: 1940 });

    expect(body).not.toContain("<Jerry>");
    expect(body).toContain("&#38;");
  });

  it("colours a title the same way every time", () => {
    const first = generatedPoster({ title: "Detour", year: 1945 }).body;

    expect(generatedPoster({ title: "Detour", year: 1945 }).body).toBe(first);
  });
});
