type WeirLogoVariant = "sidebar" | "auth";

type Props = {
  variant?: WeirLogoVariant;
  className?: string;
};

/**
 * The Weir mark (#581), inline so it follows the theme tokens. Three streams in one colour: the
 * Windows tray icon is a single-colour mask, and the sidebar never draws the mark small enough for
 * the streams to fuse (packaging/brand/README.md). The paths are generated: change the geometry in
 * design-options/logos-round4/build/mark.py and paste the path data its build.py prints.
 */
function WeirMark({ className }: { className?: string }) {
  return (
    <svg
      className={["mm-logo-mark", className].filter(Boolean).join(" ")}
      viewBox="0 0 24 24"
      aria-hidden="true"
      focusable="false"
    >
      {/* The outer stream: the vertical approach, the turn over the crest, and the flat cut
          where it tips into the tailrace. This one carries the whole silhouette. */}
      <path
        className="mm-logo-mark__stream"
        d="M3.65 16.55L3.65 13.7A9.65 9.65 0 0 1 21.889 9.3L19.74 9.3A7.8 7.8 0 0 0 5.5 13.7L5.5 16.55Z"
      />
      {/* The middle stream: a repeat of the outer one on a smaller radius, carrying no
          structure of its own. This is the band #582 dropped for 16px legibility; it is back
          here, and everywhere above 16px, because nothing else is rendered that small. */}
      <path
        className="mm-logo-mark__stream"
        d="M7.05 16.55L7.05 13.7A6.25 6.25 0 0 1 17.739 9.3L13.3 9.3A4.4 4.4 0 0 0 8.9 13.7L8.9 16.55Z"
      />
      {/* The inner stream, its quarter turn into the tailrace bar, and the apron running back
          out to the left: one outline, because a shared edge between two shapes shows up as a
          hairline seam at fractional raster sizes. */}
      <path
        className="mm-logo-mark__stream"
        d="M10.45 13.7L10.45 18.1L2.1 18.1L2.1 19.95L12.3 19.95L12.3 13.7A1 1 0 0 1 13.3 12.7L21.889 12.7L21.889 10.85L13.3 10.85A2.85 2.85 0 0 0 10.45 13.7Z"
      />
    </svg>
  );
}

/** Mark plus the "Weir" wordmark set in the app font. */
export function WeirLogo({ variant = "auth", className }: Props) {
  return (
    <span
      className={["mm-logo", `mm-logo--${variant}`, className]
        .filter(Boolean)
        .join(" ")}
      role="img"
      aria-label="Weir"
    >
      <WeirMark />
      <span className="mm-logo-wordmark" aria-hidden="true">
        Weir
      </span>
    </span>
  );
}
