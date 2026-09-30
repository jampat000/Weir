import { plural } from "../ui/mm-plural";

/** A file's history is kept for up to ten years after the file is gone; 0 keeps it for ever. */
export const FILE_HISTORY_MAX_DAYS = 3650;

/**
 * How long a file's history lasts, in one sentence: while Weir still knows the file, then for the chosen
 * number of days after it is gone or forgotten. 0 means until someone removes it.
 */
export function fileHistoryRetentionNote(days: number): string {
  return days > 0
    ? `Weir keeps this history while it still knows the file, then for ${plural(days, "day", "days")} after the file is gone.`
    : "Weir keeps this history until you remove it.";
}
