import type { ReactNode } from "react";

export type LogChip = {
  value: string;
  label: ReactNode;
  /** How many rows choosing it would show; left out while that is not known. */
  count?: number;
  /** The colour of the row dot it carries, for a chip that stands for a level. */
  level?: string;
  /** Whether the chip is pressed. */
  pressed: boolean;
};

/**
 * A short row of toggle buttons in the header's style, each one pressed or not on its own: the way a filter that may take
 * several values reads, as a segmented choice does for one. Each says whether it is pressed, so it is a filter to a screen
 * reader too.
 */
export function LogChips({
  ariaLabel,
  chips,
  onToggle,
  dataTestId,
}: {
  ariaLabel: string;
  chips: readonly LogChip[];
  onToggle: (value: string) => void;
  dataTestId?: string;
}) {
  return (
    <div
      className="mm-segmented"
      role="group"
      aria-label={ariaLabel}
      data-testid={dataTestId}
    >
      {chips.map((chip) => (
        <button
          key={chip.value}
          type="button"
          className="mm-segmented__option"
          aria-pressed={chip.pressed}
          onClick={() => onToggle(chip.value)}
        >
          {chip.level ? (
            <span
              className={`mm-log-dot mm-log-dot--${chip.level}`}
              aria-hidden="true"
            />
          ) : null}
          {chip.label}
          {chip.count === undefined ? null : (
            <>
              {" "}
              <span className="mm-segmented__count">
                {chip.count.toLocaleString()}
              </span>
            </>
          )}
        </button>
      ))}
    </div>
  );
}
