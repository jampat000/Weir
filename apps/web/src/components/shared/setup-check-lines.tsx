import type { Schema } from "../../lib/api/types";

type SetupCheckLine = Schema<"ManagerSetupLineOut">;

const MARKS: Record<SetupCheckLine["state"], string> = {
  ok: "✓",
  problem: "✗",
  note: "·",
  unverified: "?",
};

const MARK_CLASSES: Record<SetupCheckLine["state"], string> = {
  ok: "mm-status-text--healthy",
  problem: "mm-status-text--warning",
  note: "text-mm-text3",
  unverified: "text-mm-text3",
};

const SCREEN_READER_PREFIXES: Record<SetupCheckLine["state"], string> = {
  ok: "Fine: ",
  problem: "Needs a fix: ",
  note: "Note: ",
  unverified: "Not verified: ",
};

/** The lines of a setup check, one per finding, marked as fine, needing a fix, or a note. */
export function SetupCheckLines({
  label,
  lines,
}: {
  /** What was checked, e.g. "Deluno": names the list for assistive tech. */
  label: string;
  lines: SetupCheckLine[];
}) {
  return (
    <ul className="space-y-1.5 text-sm leading-5" aria-label={`${label} check`}>
      {lines.map((line, index) => (
        <li key={index} className="flex gap-2">
          <span aria-hidden="true" className={MARK_CLASSES[line.state]}>
            {MARKS[line.state]}
          </span>
          <span
            className={
              line.state === "problem"
                ? "mm-status-text--warning"
                : "text-mm-text2"
            }
          >
            <span className="sr-only">
              {SCREEN_READER_PREFIXES[line.state]}
            </span>
            {line.text}
          </span>
        </li>
      ))}
    </ul>
  );
}
