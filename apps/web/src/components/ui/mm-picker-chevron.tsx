/**
 * The accent chevron an anchored picker draws at its right edge, the same mark a native select has. It turns over while
 * the list is open.
 */
export function MmPickerChevron({ open }: { open: boolean }) {
  return (
    <svg
      aria-hidden
      className={[
        "h-4 w-4 shrink-0 text-mm-accent transition-transform",
        open ? "rotate-180" : "",
      ].join(" ")}
      viewBox="0 0 20 20"
      fill="currentColor"
    >
      <path d="M5.5 7.5 10 12l4.5-4.5H5.5z" />
    </svg>
  );
}
