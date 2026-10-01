import { useId, type ReactNode } from "react";
import { Link } from "react-router-dom";

type PanelProps = {
  title: string;
  /** A quiet line after the title. It truncates before anything else when the panel is narrow. */
  count?: ReactNode;
  /** Something before the title, such as a live dot. */
  leading?: ReactNode;
  /** Where the panel's subject is managed. Without it the header has no link. */
  to?: string;
  /** The link's words, named with the panel's title for a screen reader: "History: Just finished". */
  toLabel?: string;
  /** Shows the link as an arrow alone, so a long count keeps the room. The words are still said. */
  iconOnly?: boolean;
  className?: string;
  bodyClassName?: string;
  children: ReactNode;
};

/**
 * The card every part of a dashboard sits in: a title, a quiet count that gives way before anything
 * else, an arrow link to where the subject is managed, and a body that takes the rest of the height.
 * The header never wraps onto a second line.
 */
export function Panel({
  title,
  count,
  leading,
  to,
  toLabel,
  iconOnly = false,
  className,
  bodyClassName,
  children,
}: PanelProps) {
  const titleId = useId();
  const linkWords = toLabel ?? "Open";
  return (
    <section
      aria-labelledby={titleId}
      className={["mm-panel", className].filter(Boolean).join(" ")}
    >
      <header className="mm-panel__head">
        {leading}
        <h2 id={titleId} className="mm-panel__title">
          {title}
        </h2>
        <span
          className="mm-panel__count"
          title={typeof count === "string" ? count : undefined}
        >
          {count}
        </span>
        {to ? (
          <Link
            to={to}
            className="mm-panel__link"
            aria-label={`${linkWords}: ${title}`}
          >
            {iconOnly ? null : linkWords}
            <svg
              viewBox="0 0 24 24"
              width="14"
              height="14"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
              aria-hidden="true"
            >
              <path d="M5 12h14" />
              <path d="m12 5 7 7-7 7" />
            </svg>
          </Link>
        ) : null}
      </header>
      <div
        className={["mm-panel__body", bodyClassName].filter(Boolean).join(" ")}
      >
        {children}
      </div>
    </section>
  );
}
