import type { CSSProperties } from "react";
import { Link } from "react-router-dom";

type BarListRowProps = {
  label: string;
  /** The number at the row's right edge, already formatted. */
  value: string;
  /** How full the bar is, from 0 to 1. Out-of-range values are held to the ends. */
  fraction: number;
  /** The bar's colour, as a CSS colour or a token: `var(--mm-lane-queued)`. */
  color: string;
  /** Makes the whole row a link. */
  to?: string;
  /** The link's accessible name, which should carry the number the row shows. */
  linkName?: string;
  /** A row with nothing in it stays as a quiet empty track, so the list always reads whole. */
  empty?: boolean;
  /** Brightens the bar for a moment, for a row something just happened to. */
  lit?: boolean;
};

const clampFraction = (fraction: number) =>
  Number.isFinite(fraction) ? Math.min(1, Math.max(0, fraction)) : 0;

/** One row of a list of reasons or kinds: its label, its number, and a bar that shows its share. */
export function BarListRow({
  label,
  value,
  fraction,
  color,
  to,
  linkName,
  empty = false,
  lit = false,
}: BarListRowProps) {
  const className = [
    "mm-bar-row",
    empty ? "mm-bar-row--empty" : "",
    lit ? "mm-bar-row--lit" : "",
  ]
    .filter(Boolean)
    .join(" ");
  const style = { "--mm-bar-color": color } as CSSProperties;
  const parts = (
    <>
      <span className="mm-bar-row__label">{label}</span>
      <span className="mm-bar-row__value">{value}</span>
      <span className="mm-bar-row__track" aria-hidden="true">
        <i style={{ width: `${Math.round(clampFraction(fraction) * 100)}%` }} />
      </span>
    </>
  );
  return to ? (
    <Link to={to} className={className} style={style} aria-label={linkName}>
      {parts}
    </Link>
  ) : (
    <div className={className} style={style}>
      {parts}
    </div>
  );
}
