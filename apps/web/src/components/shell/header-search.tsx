import type { ComponentProps } from "react";

type HeaderSearchProps = Omit<ComponentProps<"input">, "type" | "className"> & {
  /** The words the box is named by, for a screen reader and for the mark's tooltip when it is only a mark. */
  label: string;
  /** Where the header is short of room: the box is a magnifier of the controls' height until it is used. */
  collapsed?: boolean;
  /** Sizes the box among the header's other controls. */
  className?: string;
};

/**
 * The search a page puts on the header's title line. Where the header is short of room it is a magnifier the size of the
 * other controls, which opens into the box when it is pressed or focused and stays open while it holds a search; it shuts
 * again on Escape or when it loses focus empty. It is one input throughout, so what is typed, and where the focus is,
 * never moves.
 */
export function HeaderSearch({
  label,
  collapsed = false,
  className,
  onKeyDown,
  ...input
}: HeaderSearchProps) {
  return (
    <label
      className={["mm-header-search", className].filter(Boolean).join(" ")}
      data-collapsed={collapsed}
      title={collapsed ? label : undefined}
    >
      <svg
        className="mm-header-search__mark"
        viewBox="0 0 24 24"
        width="16"
        height="16"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden="true"
      >
        <circle cx="11" cy="11" r="7" />
        <path d="m20 20-3.5-3.5" />
      </svg>
      <input
        {...input}
        type="search"
        className="mm-input"
        aria-label={label}
        onKeyDown={(event) => {
          onKeyDown?.(event);
          if (event.key === "Escape") event.currentTarget.blur();
        }}
      />
    </label>
  );
}
