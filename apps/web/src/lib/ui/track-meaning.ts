import type { StatusMeaning } from "./status-meaning";

/**
 * What a track's fate means wherever tracks are listed: one kept is done, one removed is drawn in the colour of
 * removal. Removing a track is what Weir is for, so red here marks the act, not a fault.
 */
export const trackMeaning = (kept: boolean): StatusMeaning =>
  kept ? "done" : "broken";
