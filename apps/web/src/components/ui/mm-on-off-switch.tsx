/** The switch is the height of the controls beside it: a page's field, or a table row's buttons (the scale in `weir-tokens.css`). */
type SwitchSize = "page" | "row";

const SWITCH_SIZE: Record<SwitchSize, { box: string; side: string }> = {
  page: { box: "h-(--mm-control-height-page)", side: "text-sm" },
  row: { box: "h-(--mm-control-height-row)", side: "text-xs" },
};

/**
 * Weir standard **On / Off** segmented control for persisted boolean settings
 * (Enable/Disable, limit hours, etc.). Use `mmActionButtonClass` from `lib/ui/mm-control-roles` for Save/Test/helpers.
 */
export function MmOnOffSwitch({
  id,
  label,
  enabled,
  disabled,
  onChange,
  layout = "default",
  size = "page",
}: {
  id: string;
  label: string;
  enabled: boolean;
  disabled: boolean;
  onChange: (v: boolean) => void;
  /** `inline`: label left, On/Off control right (single row). `control`: the control alone, for a row that names it already. */
  layout?: "default" | "inline" | "control";
  /** `row` for a switch on a row of a table, beside the row's buttons. */
  size?: SwitchSize;
}) {
  const control = (
    <div
      className={`inline-flex ${SWITCH_SIZE[size].box} w-fit shrink-0 rounded-md border border-mm-border bg-mm-surface2/40 p-0.5`}
      role="radiogroup"
      aria-labelledby={id}
    >
      {(["On", "Off"] as const).map((side) => {
        const isOn = side === "On";
        const selected = enabled === isOn;
        return (
          <button
            key={side}
            type="button"
            role="radio"
            aria-checked={selected}
            disabled={disabled}
            onClick={() => onChange(isOn)}
            className={[
              "min-w-[3.25rem] rounded-md px-3 font-medium transition-colors",
              SWITCH_SIZE[size].side,
              selected
                ? "bg-mm-accent-soft text-mm-text1 shadow-(--mm-shadow-selected)"
                : "text-mm-text2 hover:bg-mm-card-bg/70",
              disabled
                ? "cursor-not-allowed opacity-50 hover:bg-transparent"
                : "",
            ].join(" ")}
          >
            {side}
          </button>
        );
      })}
    </div>
  );

  if (layout === "control") {
    return (
      <>
        <span className="sr-only" id={id}>
          {label}
        </span>
        {control}
      </>
    );
  }

  if (layout === "inline") {
    return (
      <div className="flex w-full min-w-0 flex-row items-center justify-between gap-4">
        <span
          className="min-w-0 flex-1 text-sm font-medium text-mm-text1"
          id={id}
        >
          {label}
        </span>
        {control}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2">
      <span className="text-sm font-medium text-mm-text1" id={id}>
        {label}
      </span>
      {control}
    </div>
  );
}
