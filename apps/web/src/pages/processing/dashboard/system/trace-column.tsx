import type { CSSProperties, ReactNode } from "react";

import { CountUp } from "../../../../components/charts/count-up";
import { FitText, type Words } from "../../../../lib/ui/fit-text";

type TraceColumnProps = {
  /** The column's colour, a design token: the swatch glows in it and its trace is drawn in it. */
  colour: string;
  label: string;
  /** The big figure, which counts to each new value; null shows a dash. */
  value: number | null;
  figure: (value: number) => string;
  unit: string;
  /** A line of detail, the fullest words first: the one that fits the column's width is shown, the first as its tooltip. */
  sub: Words;
  /** The trace that fills the rest of the column. */
  children: ReactNode;
};

const NO_READING = "–";

/**
 * One reading of a card with traces: a coloured, glowing swatch with the reading's name, the figure now, a line of
 * detail, and the trace over the last ten minutes filling the rest.
 */
export function TraceColumn({
  colour,
  label,
  value,
  figure,
  unit,
  sub,
  children,
}: TraceColumnProps) {
  return (
    <div
      className="mm-sy-col"
      style={{ "--mm-sy-colour": colour } as CSSProperties}
    >
      <span className="mm-sy-col__label">
        <i aria-hidden="true" />
        {label}
      </span>
      <span className="mm-sy-col__figure">
        {value === null ? (
          NO_READING
        ) : (
          <>
            <CountUp value={value} format={figure} />
            <small>{unit}</small>
          </>
        )}
      </span>
      <FitText words={sub} className="mm-sy-col__sub" />
      {children}
    </div>
  );
}
