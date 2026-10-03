import type { ComponentProps } from "react";

import { classNames } from "../../lib/ui/class-names";
import type { StatusMeaning } from "../../lib/ui/status-meaning";
import { StatusDot } from "./status-dot";

type ChipProps = ComponentProps<"span"> & {
  /** What the state means: it draws the chip in that meaning's colour. A chip without one is a plain label. */
  meaning?: StatusMeaning;
  /** The coloured dot before the words of a status. It is decoration: the words always say the state too. */
  dot?: boolean;
};

/**
 * The one pill: a label, or with a meaning a status, drawn as a dot and a short label in that meaning's colour.
 * Colour is never the only signal, so a status always carries words.
 */
export function Chip({
  meaning,
  dot = true,
  className,
  children,
  ...rest
}: ChipProps) {
  if (meaning) {
    return (
      <span
        className={classNames("mm-chip mm-status-pill", className)}
        data-status={meaning}
        {...rest}
      >
        {dot ? <StatusDot meaning={meaning} /> : null}
        {children}
      </span>
    );
  }
  return (
    <span
      className={classNames("mm-chip mm-chip--neutral", className)}
      {...rest}
    >
      {children}
    </span>
  );
}
