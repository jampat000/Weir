/** The status dot and the space after it, which a caption's words do not have room for. */
export const STATUS_MARK_PX = 11;

/** The font the status line is set in, read from a probe laid out as the caption's status line inside `host`. */
export function statusFont(host: HTMLElement): string {
  const line = document.createElement("span");
  line.className = "mm-shelf__line mm-shelf__status";
  line.style.visibility = "hidden";
  const words = document.createElement("span");
  line.append(words);
  host.append(line);
  // The `font` shorthand reads back empty in some browsers, so the font is put together from its parts.
  const { fontStyle, fontWeight, fontSize, fontFamily } =
    getComputedStyle(words);
  line.remove();
  return `${fontStyle} ${fontWeight} ${fontSize} ${fontFamily}`;
}
