import outfit400 from "@fontsource/outfit/files/outfit-latin-400-normal.woff2?url";
import outfit500 from "@fontsource/outfit/files/outfit-latin-500-normal.woff2?url";
import outfit600 from "@fontsource/outfit/files/outfit-latin-600-normal.woff2?url";

/**
 * The startup screen, sign-in and setup — everything rendered before a person has done anything —
 * use font weights 400, 500 and 600; nothing above the fold is 700. Preloading exactly those means
 * the browser fetches them alongside the page instead of discovering them only once it has parsed
 * the stylesheet, without paying for a weight nothing above the fold uses (#719).
 */
const ABOVE_THE_FOLD_FONTS: readonly string[] = [
  outfit400,
  outfit500,
  outfit600,
];

export function preloadAboveTheFoldFonts(): void {
  for (const href of ABOVE_THE_FOLD_FONTS) {
    const link = document.createElement("link");
    link.rel = "preload";
    link.as = "font";
    link.type = "font/woff2";
    link.href = href;
    link.crossOrigin = "anonymous";
    document.head.appendChild(link);
  }
}
