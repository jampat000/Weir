import { Link } from "react-router-dom";

/** One column of the Processing board: a heading, a one-line hint, and the cards under it. */
export function Lane({
  id,
  label,
  count,
  hint,
  live,
  active,
  children,
  aside,
}: {
  id: string;
  label: string;
  count: string | number | null;
  hint: string;
  live?: boolean;
  /** Holding a file right now. Lit lanes show where the work is; an empty one stays quiet. */
  active?: boolean;
  children: React.ReactNode;
  aside?: React.ReactNode;
}) {
  return (
    <section
      className={`mm-live-lane mm-live-lane--${id}${active ? " mm-live-lane--active" : ""}`}
      aria-labelledby={`live-lane-${id}`}
      data-testid={`live-lane-${id}`}
    >
      <div className="mm-live-lane__head">
        <h2 id={`live-lane-${id}`} className="mm-live-lane__label">
          {live ? <i className="mm-live-pulse" aria-hidden="true" /> : null}
          {label}
        </h2>
        {aside ??
          (count != null ? (
            <span className="mm-live-lane__count">{count}</span>
          ) : null)}
      </div>
      <p className="mm-live-lane__hint">{hint}</p>
      {children}
    </section>
  );
}

export function EmptyLane({ children }: { children: React.ReactNode }) {
  return <p className="mm-live-lane__empty">{children}</p>;
}

/** The last line of a lane that has more than fits: a count, never a scrollbar inside the lane. */
export function More({ count, what }: { count: number; what: string }) {
  if (count <= 0) return null;
  return (
    <li className="mm-live-lane__more">
      <Link to="/system?tab=history&show=downloads">
        {count.toLocaleString()} more {what} →
      </Link>
    </li>
  );
}
