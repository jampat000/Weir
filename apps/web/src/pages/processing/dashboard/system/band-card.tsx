import type { ReactNode } from "react";

import { classNames } from "../../../../lib/ui/class-names";

type BandCardProps = {
  /** The card's name, small and uppercase, as the Live band's tiles carry theirs. */
  label: string;
  /** A quiet note at the right of the label row. */
  aside?: ReactNode;
  className?: string;
  testId?: string;
  children: ReactNode;
};

/**
 * One card of the System view's top band: a label row, and a body that takes the rest of the card's height.
 * The three cards of the band share one height and put the same parts in the same places.
 */
export function BandCard({
  label,
  aside,
  className,
  testId,
  children,
}: BandCardProps) {
  return (
    <section
      aria-label={label}
      className={classNames("mm-sy-band", className)}
      data-testid={testId}
    >
      <div className="mm-stat__top">
        <span className="mm-stat__label">{label}</span>
        {aside ? <span className="mm-stat__aside">{aside}</span> : null}
      </div>
      <div className="mm-sy-band__body">{children}</div>
    </section>
  );
}

/** What a band card says in place of its body while its reading is on its way or could not be taken. */
export function BandNote({ children }: { children: ReactNode }) {
  return <p className="mm-sy-band__note">{children}</p>;
}
