import { useCallback, useRef, useState } from "react";

import { errorMessage } from "../../../../lib/api/error-message";
import { fetchServerLogDownload } from "../../../../lib/settings/settings-api";
import {
  fetchSystemLogExport,
  type SystemLogQuery,
} from "../../../../lib/system/system-log-api";
import {
  mmActionButtonClass,
  mmListboxOptionButtonClass,
  mmListboxPanelClass,
} from "../../../../lib/ui/mm-control-roles";
import { saveBlobAs } from "../../../../lib/ui/save-file";
import { useCloseOnOutsideAndEscape } from "../../../../lib/ui/use-close-on-outside";

type Choice = "csv" | "json" | "server-log";

const CHOICES: readonly { value: Choice; label: string; note: string }[] = [
  {
    value: "csv",
    label: "Spreadsheet (CSV)",
    note: "The rows the filters show, one line each",
  },
  {
    value: "json",
    label: "JSON",
    note: "The same rows, with everything each one recorded",
  },
  {
    value: "server-log",
    label: "Whole server log",
    note: "The server log as one file, for whoever is helping",
  },
];

/** The server log file's name: the day and time it was taken, so two downloads are not mistaken for each other. */
function serverLogFileName(): string {
  return `weir-log-${new Date().toISOString().replace(/[:.]/g, "-")}.log`;
}

/**
 * Export, in the Log card's header: the list for the filters it is showing as a spreadsheet or as JSON, and the whole
 * server log as the file it is, which the list's own pages never hold.
 */
export function LogExportMenu({
  query,
  onProblem,
}: {
  query: SystemLogQuery;
  /** Called with a sentence saying what went wrong, or with null when a new try begins. */
  onProblem: (problem: string | null) => void;
}) {
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState<Choice | null>(null);
  const containerRef = useRef<HTMLDivElement | null>(null);
  const close = useCallback(() => setOpen(false), []);
  useCloseOnOutsideAndEscape(open, close, containerRef);

  async function run(choice: Choice): Promise<void> {
    setOpen(false);
    setBusy(choice);
    onProblem(null);
    try {
      if (choice === "server-log") {
        saveBlobAs(await fetchServerLogDownload(), serverLogFileName());
      } else {
        const { blob, filename } = await fetchSystemLogExport(choice, query);
        saveBlobAs(blob, filename);
      }
    } catch (error) {
      onProblem(errorMessage(error, "Could not export the log."));
    } finally {
      setBusy(null);
    }
  }

  return (
    <div ref={containerRef} className="relative">
      <button
        type="button"
        className={`${mmActionButtonClass({ variant: "secondary" })} mm-sys-btn gap-1.5`}
        aria-haspopup="menu"
        aria-expanded={open}
        disabled={busy !== null}
        data-testid="logs-export"
        onClick={() => setOpen((value) => !value)}
      >
        {busy ? "Exporting…" : "Export"}
        <svg
          viewBox="0 0 24 24"
          width="12"
          height="12"
          fill="none"
          stroke="currentColor"
          strokeWidth="2.5"
          strokeLinecap="round"
          strokeLinejoin="round"
          aria-hidden="true"
        >
          <path d="m6 9 6 6 6-6" />
        </svg>
      </button>
      {open ? (
        <div
          className={mmListboxPanelClass}
          style={{ left: "auto", right: 0, width: "17.5rem" }}
          role="menu"
          aria-label="Export"
        >
          {CHOICES.map((choice) => (
            <button
              key={choice.value}
              type="button"
              role="menuitem"
              className={mmListboxOptionButtonClass(false)}
              onClick={() => void run(choice.value)}
            >
              <span>{choice.label}</span>
              <small className="block text-mm-text3">{choice.note}</small>
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}
