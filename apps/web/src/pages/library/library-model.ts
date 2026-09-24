import { formatBytes } from "../../lib/format/bytes";
import type {
  LibraryFile,
  LibraryModeSchedule,
  LibraryProblemKind,
  LibraryTotals,
} from "../../lib/processing/library-mode-api";
import { parseAppDate } from "../../lib/ui/mm-format-date";
import { plural } from "../../lib/ui/mm-plural";

const MINUTE_MS = 60_000;
/** Past this many minutes a scan's age reads in hours, and past this many hours in days. */
const MINUTES_BEFORE_HOURS = 90;
const HOURS_BEFORE_DAYS = 36;

export const PROBLEM_LABELS: Record<LibraryProblemKind, string> = {
  seeding: "Still seeding",
  manager_redownload: "Your manager would download it again",
  no_permission: "Weir cannot write to it",
  unreadable: "Weir cannot read it",
  no_video: "No video in it",
  no_audio_left: "The rules would leave no audio",
};

/**
 * What a file belongs under: the title its media manager knows it by, or, with no manager, the folders it sits
 * in. A season folder on its own says nothing, so it is shown with the show above it.
 */
export function groupOf(file: LibraryFile): string {
  if (file.manager_title) return file.manager_title;
  const parts = file.path.split(/[\\/]/).filter(Boolean);
  const parent = parts.at(-2);
  const above = parts.at(-3);
  if (parent && above && /^(season|series)\s*\d+$/i.test(parent)) {
    return `${above} · ${parent}`;
  }
  return parent ?? parts[0] ?? "Files";
}

/** Files grouped by title, titles in alphabetical order, files in the order the server sent them. */
export function groupFiles(files: LibraryFile[]): [string, LibraryFile[]][] {
  const byTitle = new Map<string, LibraryFile[]>();
  for (const file of files) {
    const key = groupOf(file);
    const list = byTitle.get(key);
    if (list) list.push(file);
    else byTitle.set(key, [file]);
  }
  return [...byTitle.entries()].sort((a, b) => a[0].localeCompare(b[0]));
}

/** The table says what would happen in as few words as fit the column; the panel carries the whole sentence. */
export function verdictOf(file: LibraryFile): string {
  if (file.classification === "matches") return "Matches your rules";
  if (file.classification === "cannot_process") {
    const text = (
      file.reason ??
      file.summary ??
      "Weir will not touch this one"
    ).trim();
    const end = text.search(/[.!?](\s|$)/);
    return end > 0 ? text.slice(0, end) : text;
  }

  const parts: string[] = [];
  if (file.removed_audio_tracks) {
    parts.push(`${file.removed_audio_tracks} audio`);
  }
  if (file.removed_subtitle_tracks) {
    parts.push(plural(file.removed_subtitle_tracks, "subtitle", "subtitles"));
  }
  return parts.length ? `Removes ${parts.join(", ")}` : "Would change";
}

/**
 * The line under the title: how much is here, and when Weir changes a file. With the daily clean on, Weir
 * changes files without being asked, so the line must not promise otherwise.
 */
export function headerLead(
  totals: LibraryTotals | undefined,
  dailyClean: boolean,
): string {
  const when = dailyClean
    ? "cleans what would change once a day, on this library’s schedule."
    : "only changes one when you ask.";
  return totals
    ? `${totals.files.toLocaleString()} files, ${formatBytes(totals.size_bytes)} on your storage. Weir reads them where they are and ${when}`
    : `Weir reads your library where it is and ${when}`;
}

/**
 * When "Scheduled scan and clean" next runs, beside when the library was last checked; nothing while it is off.
 * A run that is due reads as starting now: the server's timer picks it up within half a minute.
 */
export function nextScheduled(
  schedule: LibraryModeSchedule | undefined,
  now: number,
  formatDate: (iso: string) => string,
): string | null {
  if (!schedule?.enabled) return null;
  if (!schedule.next_run_at) {
    return "scheduled check and clean cannot run: no library folders, or a schedule window that never opens";
  }
  return parseAppDate(schedule.next_run_at).getTime() <= now
    ? "scheduled check and clean starting now"
    : `next scheduled check and clean ${formatDate(schedule.next_run_at)}`;
}

/** How long ago the scan that these numbers come from ran. */
export function scanned(generatedAt: number | null, now: number): string {
  if (!generatedAt) return "not scanned yet";
  const minutes = Math.max(
    0,
    Math.round((now - generatedAt * 1000) / MINUTE_MS),
  );
  if (minutes < 1) return "checked just now";
  if (minutes < MINUTES_BEFORE_HOURS) return `checked ${minutes} min ago`;
  const hours = Math.round(minutes / 60);
  return hours < HOURS_BEFORE_DAYS
    ? `checked ${hours} h ago`
    : `checked ${Math.round(hours / 24)} days ago`;
}
