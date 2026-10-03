import { afterEach, expect, it } from "vitest";

import { preloadAboveTheFoldFonts } from "./preload-fonts";

afterEach(() => {
  document
    .querySelectorAll('link[rel="preload"]')
    .forEach((link) => link.remove());
});

const preloaded = () =>
  Array.from(document.querySelectorAll<HTMLLinkElement>('link[rel="preload"]'));

it("preloads the one variable face the startup screen, sign-in and setup are set in", () => {
  preloadAboveTheFoldFonts();

  expect(preloaded()).toHaveLength(1);
  preloaded().forEach((link) => {
    expect(link.as).toBe("font");
    expect(link.type).toBe("font/woff2");
    expect(link.crossOrigin).toBe("anonymous");
    expect(link.href).toMatch(/inter-latin-wght-normal/);
  });
});

it("never preloads the monospace face, which nothing above the fold uses", () => {
  preloadAboveTheFoldFonts();

  expect(preloaded().some((link) => link.href.includes("jetbrains"))).toBe(
    false,
  );
});
