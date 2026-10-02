import type { ComponentProps } from "react";

import type { MmStatusTone } from "../../lib/ui/mm-status-tone";

type ChipProps = ComponentProps<"span"> & {
  tone?: MmStatusTone;
  /** The coloured dot before the words. It is decoration: the words always say the state too. */
  dot?: boolean;
};

/**
 * The one status pill: a dot and a short label in a status tone. Colour is never the only signal, so
 * every use carries words.
 */
export function Chip({
  tone = "neutral",
  dot = true,
  className,
  children,
  ...rest
}: ChipProps) {
  return (
    <span
      className={["mm-chip", `mm-chip--${tone}`, className]
        .filter(Boolean)
        .join(" ")}
      {...rest}
    >
      {dot ? <span className="mm-chip__dot" aria-hidden="true" /> : null}
      {children}
    </span>
  );
}
