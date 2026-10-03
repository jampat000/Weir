/** How long a figure takes to reach its new value. */
export const COUNT_UP_MS = 520;

/** Fast at first and settling gently: 0 at the start, 1 at the end. */
export function easeOutCubic(progress: number): number {
  const done = Math.min(1, Math.max(0, progress));
  return 1 - (1 - done) ** 3;
}

/** The value `elapsedMs` into a count from `from` to `to` that takes `durationMs`; `to` itself once it is over. */
export function countUpValue(
  from: number,
  to: number,
  elapsedMs: number,
  durationMs: number,
): number {
  if (durationMs <= 0 || elapsedMs >= durationMs) return to;
  return from + (to - from) * easeOutCubic(elapsedMs / durationMs);
}
