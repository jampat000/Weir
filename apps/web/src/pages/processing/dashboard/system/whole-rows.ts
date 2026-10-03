/** Whole rows of tiles: the box is as tall as the rows that fit, never showing a sliver of the next. */

/** How many whole rows of `rowPx` (with `gapPx` between them) fit `roomPx`; at least one. */
export function wholeRows(
  roomPx: number,
  rowPx: number,
  gapPx: number,
): number {
  return Math.max(1, Math.floor((roomPx + gapPx) / (rowPx + gapPx)));
}

/** The height of that many rows, gaps included. */
export function rowsHeight(rows: number, rowPx: number, gapPx: number): number {
  return rows * rowPx + (rows - 1) * gapPx;
}

/** How many tiles lie beyond the `rows` that show, in a grid `columns` across. */
export function tilesBeyond(
  total: number,
  rows: number,
  columns: number,
): number {
  return Math.max(0, total - rows * Math.max(1, columns));
}

/** How many columns a grid has, from its computed `grid-template-columns` ("91px 91px"). */
export function columnsOf(template: string): number {
  const tracks = template
    .trim()
    .split(/\s+/)
    .filter((track) => /^[\d.]+px$/.test(track));
  return Math.max(1, tracks.length);
}
