/**
 * The one colour language for status in Weir: a tone drawn from the `--mm-status-*` tokens, so it
 * reads the same in dark and light. Raw palette colours are tuned for one theme and all but vanish
 * on the other, so nothing that says good, bad or waiting uses them. The `Chip` draws a tone.
 */
export type MmStatusTone =
  "healthy" | "info" | "warning" | "failed" | "neutral";
