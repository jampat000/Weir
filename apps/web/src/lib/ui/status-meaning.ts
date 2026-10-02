/**
 * What a status means, whatever the feature that shows it. A feature decides which meaning a state has; the
 * colour of a meaning is decided once, in weir-tokens.css (--mm-status-*) and drawn by weir-status.css, so
 * "green" is the same green on every screen and no component picks a colour for a status.
 *
 *   done       finished, answering, in sync, nothing to do
 *   todo       waiting its turn
 *   doing      being worked on right now
 *   attention  held back, slow, or needs a look
 *   broken     failed, or Weir cannot read or reach it
 *   idle       left alone on purpose, off, or only information
 *
 * To show one, put `data-status={meaning}` on the element and add the class that draws it: `mm-status-dot` (or
 * `<StatusDot meaning />`), `mm-status-text` for a coloured word, or `mm-status-pill` on a chip. A figure for
 * space saved is not a status: give it the class `mm-payoff`.
 */
export type StatusMeaning =
  "done" | "todo" | "doing" | "attention" | "broken" | "idle";

export const STATUS_MEANINGS: readonly StatusMeaning[] = [
  "done",
  "todo",
  "doing",
  "attention",
  "broken",
  "idle",
];

/** Whether a status asks the person to look: it is held back, slow or unreadable, or it has failed. */
export const needsYou = (meaning: StatusMeaning): boolean =>
  meaning === "attention" || meaning === "broken";
