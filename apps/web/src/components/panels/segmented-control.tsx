import type { ReactNode } from "react";

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
};

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
}: SegmentedControlProps<Value>) {
  return (
    <div
      className="mm-segmented"
      role="group"
      aria-label={ariaLabel}
      data-testid={dataTestId}
    >
      {options.map((option) => (
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
    </div>
  );
}
