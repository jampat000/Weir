/**
 * Control roles across Weir. Primary commits (Save, Apply, Confirm); secondary runs a utility
 * (Test, Open, Run now, Retry); tertiary is a lower-emphasis helper (Show, Clear, row actions).
 * Red is for actions that remove, override or step outside normal running; Weir's routine work (cleaning, scans,
 * cleanup runs) is never red. Such an action is `danger` when it is the confirming button of a dialog (filled, so the
 * choice is unmistakable) and `danger-outline` when it sits among other buttons. An on/off choice uses `MmOnOffSwitch`,
 * not these classes. A button is one of three heights (the scale in `weir-tokens.css`): `header` in a page's header,
 * `card` inside a card (the default), `row` for an action on a row of a list or table.
 */

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-mm-accent-ring focus-visible:ring-offset-2 focus-visible:ring-offset-mm-card-bg";

const BUTTON_LAYOUT =
  "inline-flex max-w-full items-center justify-center rounded-mm-control border leading-snug tracking-normal transition-all duration-150 whitespace-normal text-center";

export type MmActionSize = "header" | "card" | "row";

/** A button's height is a minimum: a long label wraps and grows the button rather than clipping. */
const BUTTON_SIZE: Record<MmActionSize, string> = {
  header: "min-h-(--mm-control-height-page) px-3.5 py-1 text-sm",
  card: "min-h-(--mm-control-height-sm) px-3.5 py-1 text-sm",
  row: "min-h-(--mm-control-height-row) px-2.5 py-0.5 text-xs",
};

/**
 * Default class for ordinary editable text inputs (paths, titles, CSV tokens, etc.).
 * Use the UI body font — not monospace unless the value is truly technical read-only display.
 */
export const mmEditableTextFieldClass = "mm-input w-full min-w-0";

/**
 * Shared layout/interaction for native selects and anchored listbox triggers.
 * Visual chrome (inset, border, height, padding, focus) lives on `.mm-input` in `weir-forms.css`.
 */
const mmNativeFieldShell =
  "mm-input w-full min-w-0 text-sm text-mm-text focus-visible:outline-none disabled:cursor-not-allowed";

/** A native `<select>` under a field label; includes the top spacing. */
export const mmSelectFieldClass = `${mmNativeFieldShell} mt-1 cursor-pointer`;

/** Anchored picker button (custom listbox) — visually aligned with {@link mmSelectFieldClass}.
 *  `mm-input--opens` gives it the quiet picker box a native select has; it draws its own chevron in markup. */
export const mmPickerTriggerClass = `${mmNativeFieldShell} mm-input--opens mt-1 cursor-pointer text-left`;

/** The trigger as the Dashboard's pickers draw it: a field's height with the label at the left and the chevron at the
 *  right. `.mm-input` draws the focused field's colours while `aria-expanded` says its list is open. */
export const mmPickerTriggerSurface = `${mmPickerTriggerClass} flex items-center justify-between gap-2`;

/** Checkbox control — used for multi-option rows and standalone toggles. */
export const mmCheckboxControlClass =
  "mt-0.5 h-4 w-4 shrink-0 rounded border-mm-border text-mm-primary accent-mm-primary " +
  "focus:outline-none focus-visible:ring-2 focus-visible:ring-mm-accent-ring focus-visible:ring-offset-2 focus-visible:ring-offset-mm-card-bg " +
  "disabled:cursor-not-allowed disabled:opacity-50";

/** Dropdown panel for {@link mmPickerTriggerClass} — matches Global Settings timezone listbox. */
export const mmListboxPanelClass =
  "absolute z-20 mt-1 max-h-64 w-full min-w-0 overflow-auto rounded-mm-field border border-mm-border bg-mm-card-bg py-1 shadow-lg";

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

export type MmActionVariant =
  "primary" | "secondary" | "tertiary" | "danger" | "danger-outline";

/** What each variant looks like and how it answers hover, focus and a disabled button. */
const BUTTON_LOOK: Record<MmActionVariant, string> = {
  primary: [
    "font-semibold cursor-pointer border-mm-primary bg-mm-primary text-mm-on-accent shadow-(--mm-shadow-action)",
    "hover:border-mm-primary-bright hover:bg-mm-primary-bright hover:shadow-(--mm-shadow-action-hover) hover:-translate-y-px",
    "active:translate-y-0 active:brightness-[0.97]",
    FOCUS_RING,
    "disabled:cursor-not-allowed disabled:border-mm-border disabled:bg-mm-button-quiet-bg disabled:text-mm-text3 disabled:opacity-80 disabled:shadow-none",
    "disabled:hover:border-mm-border disabled:hover:bg-mm-button-quiet-bg disabled:hover:shadow-none disabled:hover:translate-y-0",
  ].join(" "),
  secondary: [
    "font-semibold cursor-pointer border-mm-border bg-mm-button-secondary-bg text-mm-text",
    "hover:border-[color-mix(in_srgb,var(--mm-primary)_55%,transparent)] hover:bg-mm-accent-soft hover:shadow-sm",
    "active:brightness-[0.97]",
    FOCUS_RING,
    "disabled:cursor-not-allowed disabled:border-mm-border disabled:bg-transparent disabled:text-mm-text3 disabled:opacity-70 disabled:shadow-none",
    "disabled:hover:border-mm-border disabled:hover:bg-transparent disabled:hover:shadow-none",
  ].join(" "),
  tertiary: [
    "font-medium cursor-pointer border-mm-border bg-transparent text-mm-text2",
    "hover:border-mm-border hover:bg-mm-card-bg/55 hover:text-mm-text1",
    "active:brightness-[0.97]",
    FOCUS_RING,
    "disabled:cursor-not-allowed disabled:border-mm-border disabled:bg-transparent disabled:text-mm-text3 disabled:opacity-60",
    "disabled:hover:border-mm-border disabled:hover:bg-transparent disabled:hover:text-mm-text3",
  ].join(" "),
  danger: [
    "font-semibold cursor-pointer border-mm-destructive bg-mm-destructive text-mm-on-accent",
    "hover:border-mm-status-failed-text hover:bg-mm-status-failed-text",
    "active:brightness-[0.97]",
    FOCUS_RING,
    "disabled:cursor-not-allowed disabled:border-mm-border disabled:bg-mm-button-quiet-bg disabled:text-mm-text3 disabled:opacity-80",
    "disabled:hover:border-mm-border disabled:hover:bg-mm-button-quiet-bg",
  ].join(" "),
  "danger-outline": [
    "font-semibold cursor-pointer border-mm-destructive bg-transparent text-mm-status-failed-text",
    "hover:bg-mm-status-failed-bg",
    "active:brightness-[0.97]",
    FOCUS_RING,
    "disabled:cursor-not-allowed disabled:border-mm-border disabled:bg-transparent disabled:text-mm-text3 disabled:opacity-70",
    "disabled:hover:border-mm-border disabled:hover:bg-transparent",
  ].join(" "),
};

/**
 * The classes for one action button. A disabled button looks disabled from the element's own state,
 * so `<button disabled>` is the whole story. `disabled:hover:*` repeats each hover property: CSS
 * `:hover` matches a disabled button, and the doubled variant outranks the plain hover on specificity,
 * so the outcome does not depend on the order Tailwind emits its variants in.
 */
export function mmActionButtonClass(opts: {
  variant: MmActionVariant;
  size?: MmActionSize;
}): string {
  const { variant, size = "card" } = opts;
  return [BUTTON_LAYOUT, BUTTON_SIZE[size], BUTTON_LOOK[variant]].join(" ");
}
