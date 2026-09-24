import { useCallback, useId, useRef, useState } from "react";
import {
  mmListboxOptionButtonClass,
  mmListboxPanelClass,
  mmPickerTriggerClass,
} from "../../lib/ui/mm-control-roles";
import { useCloseOnOutsideAndEscape } from "../../lib/ui/use-close-on-outside";
import { useListboxKeyboardNav } from "./use-listbox-keyboard-nav";

export type MmListboxOption = { value: string; label: string };

type MmListboxPickerProps = {
  options: readonly MmListboxOption[];
  value: string;
  onChange: (next: string) => void;
  disabled?: boolean;
  placeholder?: string;
  "data-testid"?: string;
  /** Use with a visible `<span id={…}>` label (same pattern as Global Settings timezone). */
  ariaLabelledBy?: string;
  ariaDescribedBy?: string;
  /** Optional width constraint on the anchor (e.g. `max-w-md`). */
  className?: string;
};

/**
 * Custom listbox picker — **visual and interaction parity** with Global Settings → Timezone
 * (anchored panel, option rows, click-outside, Escape).
 */
export function MmListboxPicker({
  options,
  value,
  onChange,
  disabled = false,
  placeholder = "Select…",
  "data-testid": dataTestId,
  ariaLabelledBy,
  ariaDescribedBy,
  className,
}: MmListboxPickerProps) {
  const autoId = useId();
  const listboxId = `${autoId}-listbox`;
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement | null>(null);
  const triggerRef = useRef<HTMLButtonElement | null>(null);
  const close = useCallback(() => setOpen(false), []);
  useCloseOnOutsideAndEscape(open, close, containerRef);

  const selectedIndex = options.findIndex((o) => o.value === value);
  const nav = useListboxKeyboardNav(options, {
    isOpen: open,
    initialIndex: Math.max(selectedIndex, 0),
    onActivate: (option) => {
      onChange(option.value);
      setOpen(false);
      triggerRef.current?.focus();
    },
  });

  const selected = options.find((o) => o.value === value);
  const triggerLabel = selected?.label ?? placeholder;
  const triggerSurface = [
    mmPickerTriggerClass,
    "flex min-h-[2.5rem] items-center justify-between gap-2",
    open && !disabled
      ? "border-mm-input-border-focus !shadow-[inset_0_1px_3px_rgba(0,0,0,0.22),inset_0_1px_0_rgba(255,255,255,0.04),0_0_0_2px_var(--mm-input-focus-ring)]"
      : "",
  ]
    .filter(Boolean)
    .join(" ");

  return (
    <div
      ref={containerRef}
      className={["relative", className].filter(Boolean).join(" ")}
    >
      <button
        ref={triggerRef}
        type="button"
        className={triggerSurface}
        disabled={disabled}
        aria-haspopup="listbox"
        aria-controls={open ? listboxId : undefined}
        aria-expanded={open}
        aria-labelledby={ariaLabelledBy}
        aria-describedby={ariaDescribedBy}
        data-testid={dataTestId}
        onClick={() => {
          if (!disabled) {
            setOpen((v) => !v);
          }
        }}
        onKeyDown={nav.onKeyDown}
      >
        <span className="min-w-0 max-h-16 flex-1 overflow-y-auto whitespace-normal break-words text-left">
          {triggerLabel}
        </span>
        <svg
          aria-hidden
          className={[
            // The same accent mark a native select draws, so an anchored listbox and a select read alike.
            "h-4 w-4 shrink-0 text-mm-accent transition-transform",
            open ? "rotate-180" : "",
          ].join(" ")}
          viewBox="0 0 20 20"
          fill="currentColor"
        >
          <path d="M5.5 7.5 10 12l4.5-4.5H5.5z" />
        </svg>
      </button>
      {open ? (
        <div
          id={listboxId}
          className={mmListboxPanelClass}
          role="listbox"
          tabIndex={-1}
          onKeyDown={nav.onKeyDown}
        >
          {options.map((opt, index) => (
            <button
              key={opt.value === "" ? "__empty__" : opt.value}
              ref={nav.registerOption(index)}
              type="button"
              role="option"
              tabIndex={index === nav.activeIndex ? 0 : -1}
              aria-selected={opt.value === value}
              className={[
                mmListboxOptionButtonClass(opt.value === value),
                index === nav.activeIndex
                  ? "ring-1 ring-inset ring-mm-accent-ring"
                  : "",
              ]
                .filter(Boolean)
                .join(" ")}
              onMouseDown={(e) => {
                e.preventDefault();
                e.stopPropagation();
              }}
              onClick={(e) => {
                e.preventDefault();
                e.stopPropagation();
                nav.setActive(index);
                onChange(opt.value);
                setOpen(false);
                triggerRef.current?.focus();
              }}
            >
              {opt.label}
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}
