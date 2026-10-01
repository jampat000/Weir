import interLatin from "@fontsource-variable/inter/files/inter-latin-wght-normal.woff2?url";

/**
 * The startup screen, sign-in and setup — everything rendered before a person has done anything —
 * are set in Inter's Latin range. One variable file carries every weight, so preloading it means the
 * browser fetches the face alongside the page instead of discovering it only once it has parsed the
 * stylesheet (#719). The monospace face is for keyboard hints and code, never above the fold.
 */
const ABOVE_THE_FOLD_FONTS: readonly string[] = [interLatin];

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
