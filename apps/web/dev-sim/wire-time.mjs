/** Timestamps as the server writes them: UTC, whole seconds, a trailing Z. */
export function toWire(epochMs) {
  return new Date(epochMs).toISOString().replace(/\.\d{3}Z$/, "Z");
}

/** The epoch ms of a wire timestamp, or null when it is missing or unreadable. */
export function fromWire(text) {
  if (!text) return null;
  const ms = Date.parse(text);
  return Number.isNaN(ms) ? null : ms;
}

export const SECOND_MS = 1000;
export const MINUTE_MS = 60 * SECOND_MS;
export const HOUR_MS = 60 * MINUTE_MS;
export const DAY_MS = 24 * HOUR_MS;
