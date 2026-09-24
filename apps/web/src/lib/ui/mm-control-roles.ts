/**
 * Control roles across Weir. Primary commits (Save, Apply, Confirm); secondary runs a utility
 * (Test, Open, Run now, Retry); tertiary is a lower-emphasis helper (Show, Clear, row actions).
 * An on/off choice uses `MmOnOffSwitch`, not these classes.
 */

const actionBase =
  "inline-flex min-h-[2.5rem] max-w-full items-center justify-center rounded-md border px-4 py-2.5 text-sm font-semibold leading-snug tracking-normal transition-all duration-150 whitespace-normal text-center";

const tertiaryBase =
  "inline-flex min-h-[2.25rem] max-w-full items-center justify-center rounded-md border px-3 py-1.5 text-sm font-medium leading-snug tracking-normal transition-all duration-150 whitespace-normal text-center";

/**
 * Default class for ordinary editable text inputs (paths, titles, CSV tokens, etc.).
 * Use the UI body font — not monospace unless the value is truly technical read-only display.
 */
export const mmEditableTextFieldClass = "mm-input w-full min-w-0";

/**
 * Shared layout/interaction for native selects and anchored listbox triggers.
 * Visual chrome (inset, border, height, padding, focus) lives on `.mm-input` in `weir-shell.css`.
 */
const mmNativeFieldShell =
  "mm-input w-full min-w-0 text-sm text-mm-text transition-[border-color,background-color,box-shadow] duration-150 " +
  "focus-visible:outline-none disabled:cursor-not-allowed";

/** A native `<select>` under a field label; includes the top spacing. */
export const mmSelectFieldClass = `${mmNativeFieldShell} mt-1 cursor-pointer`;

/** Anchored picker button (custom listbox) — visually aligned with {@link mmSelectFieldClass}.
 *  `mm-input--opens` gives it the same tinted well a native select has, so everything that opens
 *  something looks alike; it draws its own chevron in markup, so the well comes without the mark. */
export const mmPickerTriggerClass = `${mmNativeFieldShell} mm-input--opens mt-1 cursor-pointer text-left`;

/** Checkbox control — used for multi-option rows and standalone toggles. */
export const mmCheckboxControlClass =
  "mt-0.5 h-4 w-4 shrink-0 rounded border-mm-border text-mm-gold accent-mm-gold " +
  "focus:outline-none focus-visible:ring-2 focus-visible:ring-mm-accent-ring focus-visible:ring-offset-2 focus-visible:ring-offset-mm-card-bg " +
  "disabled:cursor-not-allowed disabled:opacity-50";

/** Dropdown panel for {@link mmPickerTriggerClass} — matches Global Settings timezone listbox. */
export const mmListboxPanelClass =
  "absolute z-20 mt-1 max-h-64 w-full min-w-0 overflow-auto rounded border border-mm-border bg-mm-card-bg py-1 shadow-lg";

export function mmListboxOptionButtonClass(selected: boolean): string {
  return [
    "block w-full px-3 py-2 text-left text-sm transition-colors",
    selected
      ? "bg-mm-accent-soft text-mm-text1"
      : "text-mm-text2 hover:bg-mm-accent-soft hover:text-mm-text1",
  ].join(" ");
}

/** Monospace for read-only technical strings (resolved paths, env keys, raw ids). */
export const mmTechnicalMonoSmallClass =
  "font-mono text-xs break-all text-mm-text2";

/** In-page section tabs (e.g. module Overview / Connections). Not sidebar navigation. */
export function mmSectionTabClass(active: boolean): string {
  return [
    "inline-flex min-h-[2.25rem] shrink-0 items-center justify-center whitespace-nowrap rounded-md border px-3 py-1.5 text-sm font-medium transition-colors",
    "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-mm-accent-ring focus-visible:ring-offset-2 focus-visible:ring-offset-mm-bg-main",
    active
      ? "border-mm-gold bg-mm-accent-soft text-mm-text"
      : "border-mm-border bg-transparent text-mm-text2 hover:bg-mm-card-bg",
  ].join(" ");
}

/**
 * The classes for one action button. A disabled button looks disabled from the element's own state,
 * so `<button disabled>` is the whole story. `disabled:hover:*` repeats each hover property: CSS
 * `:hover` matches a disabled button, and the doubled variant outranks the plain hover on specificity,
 * so the outcome does not depend on the order Tailwind emits its variants in.
 */
export function mmActionButtonClass(opts: {
  variant: "primary" | "secondary" | "tertiary";
}): string {
  const { variant } = opts;

  if (variant === "tertiary") {
    return [
      tertiaryBase,
      "cursor-pointer border-mm-border bg-transparent text-mm-text2",
      "hover:border-mm-border hover:bg-mm-card-bg/55 hover:text-mm-text1",
      "active:brightness-[0.98]",
      "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-mm-accent-ring focus-visible:ring-offset-2 focus-visible:ring-offset-mm-card-bg",
      "disabled:cursor-not-allowed disabled:border-mm-border disabled:bg-transparent disabled:text-mm-text3 disabled:opacity-60",
      "disabled:hover:border-mm-border disabled:hover:bg-transparent disabled:hover:text-mm-text3",
    ].join(" ");
  }

  if (variant === "primary") {
    return [
      actionBase,
      "cursor-pointer border-mm-gold bg-[color-mix(in_srgb,var(--mm-gold)_20%,transparent)] text-mm-text shadow-[0_2px_14px_color-mix(in_srgb,var(--mm-gold)_14%,transparent)]",
      "hover:border-mm-gold-bright hover:bg-[color-mix(in_srgb,var(--mm-gold)_28%,transparent)] hover:shadow-[0_4px_20px_color-mix(in_srgb,var(--mm-gold)_22%,transparent)] hover:-translate-y-px",
      "active:translate-y-0 active:brightness-[0.97]",
      "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-mm-accent-ring focus-visible:ring-offset-2 focus-visible:ring-offset-mm-card-bg",
      "disabled:cursor-not-allowed disabled:border-mm-border disabled:bg-mm-button-quiet-bg disabled:text-mm-text3 disabled:opacity-80 disabled:shadow-none",
      "disabled:hover:border-mm-border disabled:hover:bg-mm-button-quiet-bg disabled:hover:shadow-none disabled:hover:translate-y-0",
    ].join(" ");
  }

  return [
    actionBase,
    "cursor-pointer border-mm-border bg-mm-button-secondary-bg text-mm-text",
    "hover:border-[color-mix(in_srgb,var(--mm-gold)_55%,transparent)] hover:bg-mm-accent-soft hover:shadow-sm",
    "active:brightness-[0.97]",
    "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-mm-accent-ring focus-visible:ring-offset-2 focus-visible:ring-offset-mm-card-bg",
    "disabled:cursor-not-allowed disabled:border-mm-border disabled:bg-transparent disabled:text-mm-text3 disabled:opacity-70 disabled:shadow-none",
    "disabled:hover:border-mm-border disabled:hover:bg-transparent disabled:hover:shadow-none",
  ].join(" ");
}
