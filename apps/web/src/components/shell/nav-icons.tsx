import type { ReactNode } from "react";

export type NavGlyphName =
  | "processing"
  | "activity"
  | "library"
  | "workflows"
  | "rules"
  | "managers"
  | "performance"
  | "system";

const GLYPH_PATHS: Record<NavGlyphName, ReactNode> = {
  processing: (
    <>
      <path d="M4 13h6V4H4z" />
      <path d="M14 20h6V4h-6z" />
      <path d="M4 20h6v-3H4z" />
    </>
  ),
  activity: (
    <>
      <path d="M4 14h5l3-8 3 12 2-4h3" />
      <path d="M4 20h16" />
    </>
  ),
  library: (
    <>
      <path d="M3 7h7l2 2h9v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" />
      <path d="M7 13h10" />
      <path d="M7 16h6" />
    </>
  ),
  workflows: (
    <>
      <circle cx="6" cy="8" r="2" />
      <circle cx="18" cy="16" r="2" />
      <path d="M8 8h4a4 4 0 0 1 4 4v2" />
    </>
  ),
  rules: (
    <>
      <rect x="5" y="5" width="14" height="4" rx="1.5" />
      <rect x="3" y="10" width="18" height="4" rx="1.5" />
      <rect x="6" y="15" width="12" height="4" rx="1.5" />
    </>
  ),
  managers: (
    <>
      <path d="M6 4h12v8H6z" />
      <path d="M8 16h8" />
      <path d="M12 12v8" />
    </>
  ),
  performance: (
    <>
      <path d="M12 5v14" />
      <path d="M5 12h14" />
      <path d="M8 8l8 8" />
      <path d="M16 8l-8 8" />
    </>
  ),
  system: (
    <>
      <path d="M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8z" />
      <path d="M4 12h2" />
      <path d="M18 12h2" />
      <path d="M12 4v2" />
      <path d="M12 18v2" />
    </>
  ),
};

/** One side-menu icon: a 24px line glyph that takes its colour from the link around it. */
export function NavGlyph({ name }: { name: NavGlyphName }) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2.2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      {GLYPH_PATHS[name]}
    </svg>
  );
}

type IconProps = { className?: string };

function Chevron({ d, className = "" }: IconProps & { d: string }) {
  return (
    <svg
      className={className}
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      aria-hidden="true"
    >
      <path
        d={d}
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}

export function NavIconChevronLeft({ className }: IconProps) {
  return <Chevron className={className} d="m14.5 6-6 6 6 6" />;
}

export function NavIconChevronRight({ className }: IconProps) {
  return <Chevron className={className} d="m9.5 6 6 6-6 6" />;
}

export function NavIconChevronDown({ className }: IconProps) {
  return <Chevron className={className} d="m6 9 6 6 6-6" />;
}

export function NavIconSignOut({ className = "" }: IconProps) {
  return (
    <svg
      className={className}
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      aria-hidden="true"
    >
      <path
        d="M14 4H6.8A1.8 1.8 0 0 0 5 5.8v12.4A1.8 1.8 0 0 0 6.8 20H14"
        stroke="currentColor"
        strokeWidth="1.75"
        strokeLinecap="round"
      />
      <path
        d="M11 12h8m0 0-3.2-3.2M19 12l-3.2 3.2"
        stroke="currentColor"
        strokeWidth="1.75"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}
