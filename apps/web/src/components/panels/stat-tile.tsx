import type { ReactNode, Ref } from "react";
import { Link } from "react-router-dom";

import { FitText } from "../../lib/ui/fit-text";

type StatTileProps = {
  label: string;
  /** The quiet note at the right of the label row. */
  aside?: ReactNode;
  /** The big figure: a number, with `StatUnit` and `StatSide` for the small words beside it. */
  figure: ReactNode;
  /** Makes the label a link to where the figure comes from. */
  to?: string;
  /** The link's accessible name, so it is not just the label: "Activity: Today". */
  linkName?: string;
  /** The body under the figure, for a tile that measures how many of its rows fit. */
  bodyRef?: Ref<HTMLDivElement>;
  className?: string;
  children: ReactNode;
};

/**
 * One tile of a band: a label row (the name, and a quiet aside), a big figure, and a body that takes the
 * rest. Tiles in a band share one height and the same parts in the same places.
 */
export function StatTile({
  label,
  aside,
  figure,
  to,
  linkName,
  bodyRef,
  className,
  children,
}: StatTileProps) {
  return (
    <section
      aria-label={label}
      className={["mm-stat", className].filter(Boolean).join(" ")}
    >
      <div className="mm-stat__top">
        {to ? (
          <Link to={to} aria-label={linkName} className="mm-stat__label">
            {label}
          </Link>
        ) : (
          <span className="mm-stat__label">{label}</span>
        )}
        {aside ? <span className="mm-stat__aside">{aside}</span> : null}
      </div>
      <div className="mm-stat__figure">{figure}</div>
      <div ref={bodyRef} className="mm-stat__body">
        {children}
      </div>
    </section>
  );
}

/** The small words after a figure: "GB saved", "at once". */
export function StatUnit({ children }: { children: ReactNode }) {
  return <small className="mm-stat__unit">{children}</small>;
}

/**
 * The small words after a figure that has others after it: they take the room the figure and the others leave (so the
 * others stand at the right edge), say themselves whole where that is there and are left out where it is not, never
 * cut. Screen readers always have them.
 */
export function StatFitUnit({ children }: { children: string }) {
  return (
    <>
      <span className="sr-only">{children}</span>
      <FitText
        ariaHidden
        className="mm-stat__unit flex-1"
        words={[children, ""]}
        title={children}
      />
    </>
  );
}

/** A second figure beside the first, in gold: "12 GB saved". */
export function StatSide({ children }: { children: ReactNode }) {
  return <span className="mm-stat__side mm-payoff">{children}</span>;
}
