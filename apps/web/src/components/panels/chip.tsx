import type { ComponentProps } from "react";

import { classNames } from "../../lib/ui/class-names";
import type { MmStatusTone } from "../../lib/ui/mm-status-tone";
import type { StatusMeaning } from "../../lib/ui/status-meaning";
import { StatusDot } from "./status-dot";

type ChipProps = ComponentProps<"span"> & {
  tone?: MmStatusTone;
  /** What the state means: it draws the chip in that meaning's colour, and the `tone` is not used. */
  meaning?: StatusMeaning;
  /** The coloured dot before the words. It is decoration: the words always say the state too. */
  dot?: boolean;
};

/**
 * The one status pill: a dot and a short label in a status colour. Colour is never the only signal, so
 * every use carries words.
 */
export function Chip({
  tone = "neutral",
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
      className={classNames("mm-chip", `mm-chip--${tone}`, className)}
      {...rest}
    >
      {dot ? <span className="mm-chip__dot" aria-hidden="true" /> : null}
      {children}
    </span>
  );
}
