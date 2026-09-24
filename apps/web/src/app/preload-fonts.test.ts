import { afterEach, expect, it } from "vitest";

import { preloadAboveTheFoldFonts } from "./preload-fonts";

afterEach(() => {
  document
    .querySelectorAll('link[rel="preload"]')
    .forEach((link) => link.remove());
});

it("preloads the font weights the startup screen, sign-in and setup use", () => {
  preloadAboveTheFoldFonts();

  const links = Array.from(
    document.querySelectorAll<HTMLLinkElement>('link[rel="preload"]'),
  );

  expect(links).toHaveLength(3);
  links.forEach((link) => {
    expect(link.as).toBe("font");
    expect(link.type).toBe("font/woff2");
    expect(link.crossOrigin).toBe("anonymous");
    expect(link.href).toMatch(/outfit-latin-(400|500|600)-normal/);
  });
});

it("never preloads the 700 weight, which nothing above the fold uses", () => {
  preloadAboveTheFoldFonts();

  const hrefs = Array.from(
    document.querySelectorAll<HTMLLinkElement>('link[rel="preload"]'),
  ).map((link) => link.href);

  expect(hrefs.some((href) => href.includes("700"))).toBe(false);
});
