import type { ReactNode } from "react";

import { MoreMenu } from "../shell/more-menu";

export type SegmentedOption<Value extends string> = Readonly<{
  value: Value;
  label: ReactNode;
}>;

type SegmentedControlProps<Value extends string> = {
  options: readonly SegmentedOption<Value>[];
  value: Value;
  onChange: (value: Value) => void;
  /** Names the group for a screen reader: "Show work from". */
  ariaLabel: string;
  dataTestId?: string;
  /** Options folded into a "More" menu at the end of the row, for a row short of room; `menuLabel` names that menu. */
  fold?: { hidden: ReadonlySet<Value>; menuLabel: string };
};

const NOTHING_FOLDED: ReadonlySet<never> = new Set();

/**
 * A short exclusive choice (two to four options) as a row of toggle buttons, in the header's style.
 * Each option is a real button that says whether it is pressed, so a filter reads as one.
 */
export function SegmentedControl<Value extends string>({
  options,
  value,
  onChange,
  ariaLabel,
  dataTestId,
  fold,
}: SegmentedControlProps<Value>) {
  const hidden: ReadonlySet<Value> = fold?.hidden ?? NOTHING_FOLDED;
  const shown = options.filter((option) => !hidden.has(option.value));
  const folded = options
    .filter((option) => hidden.has(option.value))
    .map((option) => ({ id: option.value, label: option.label }));
  return (
    <div
      className="mm-segmented"
      role="group"
      aria-label={ariaLabel}
      data-testid={dataTestId}
    >
      {shown.map((option) => (
        <button
          key={option.value}
          type="button"
          className="mm-segmented__option"
          aria-pressed={option.value === value}
          onClick={() => onChange(option.value)}
        >
          {option.label}
        </button>
      ))}
      {fold && folded.length > 0 ? (
        <MoreMenu
          menuLabel={fold.menuLabel}
          folded={folded}
          onChoose={onChange}
          buttonClassName="mm-segmented__option"
        />
      ) : null}
    </div>
  );
}
