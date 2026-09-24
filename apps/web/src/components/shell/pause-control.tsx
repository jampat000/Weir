import { useRef, useState } from "react";

import { useCanEdit } from "../../lib/auth/can-edit";
import { useCloseOnOutsideAndEscape } from "../../lib/ui/use-close-on-outside";
import { useSavePause, usePauseQuery } from "../../lib/pause/pause-queries";
import type { PauseWrite } from "../../lib/pause/pause-api";

/** Minutes offered for a pause that lifts itself. */
const DURATIONS: { label: string; minutes: number | null }[] = [
  { label: "30 minutes", minutes: 30 },
  { label: "2 hours", minutes: 120 },
  { label: "8 hours", minutes: 480 },
  { label: "Until I resume", minutes: null },
];

/** Which action last failed, so the alert names it rather than saying "something went wrong". */
type FailedAction = "pause" | "resume" | "save";

const FAILURE_TEXT: Record<FailedAction, string> = {
  pause: "Weir couldn't pause. Try again.",
  resume: "Weir couldn't resume. Try again.",
  save: "Weir couldn't save that. Try again.",
};

function pausedAnnouncement(duration: (typeof DURATIONS)[number]): string {
  return duration.minutes === null
    ? "Paused until you resume it."
    : `Paused for ${duration.label}.`;
}

/**
 * Pause processing, from every page's title row: the reason to reach for it, a busy machine,
 * has nothing to do with which screen you are on. It shares `.mm-head-control` with the theme
 * switch so the two are exactly the same height.
 */
export function PauseControl() {
  const editable = useCanEdit();
  const pause = usePauseQuery();
  const save = useSavePause();
  const [open, setOpen] = useState(false);
  const [failed, setFailed] = useState<FailedAction | null>(null);
  const [announcement, setAnnouncement] = useState("");
  const containerRef = useRef<HTMLDivElement>(null);

  useCloseOnOutsideAndEscape(open, () => setOpen(false), containerRef);

  const state = pause.data;
  if (!state) return null;

  // The menu closes only once the server confirms the change; a failure leaves it open with the
  // reason why, instead of hiding the choice the person just made (#697).
  function attempt(
    action: FailedAction,
    body: PauseWrite,
    onDone?: () => void,
  ) {
    setFailed(null);
    save.mutate(body, {
      onSuccess: () => onDone?.(),
      onError: () => setFailed(action),
    });
  }

  const resume = () =>
    attempt("resume", {
      paused: false,
      scan_while_paused: state.scan_while_paused,
    });

  if (state.paused) {
    return (
      <div className="mm-pause-control" data-testid="pause-control">
        <span className="mm-pause-badge" data-testid="pause-badge">
          Paused
        </span>
        {/* The reason carries the expiry, so an operator never has to guess how long. */}
        <span className="mm-pause-reason" data-testid="pause-reason">
          {state.reason}
        </span>
        {announcement ? (
          <span
            className="sr-only"
            role="status"
            data-testid="pause-announcement"
          >
            {announcement}
          </span>
        ) : null}
        {editable ? (
          <button
            type="button"
            className="mm-head-control"
            data-testid="pause-resume"
            disabled={save.isPending}
            onClick={resume}
          >
            {save.isPending ? "Resuming…" : "Resume"}
          </button>
        ) : null}
        {failed ? (
          <p
            className="mm-pause-alert mm-status-text--failed"
            role="alert"
            data-testid="pause-alert"
          >
            {FAILURE_TEXT[failed]}
          </p>
        ) : null}
      </div>
    );
  }

  if (!editable) return null;

  return (
    <div
      className="mm-pause-control"
      data-testid="pause-control"
      ref={containerRef}
    >
      <button
        type="button"
        className="mm-head-control"
        data-testid="pause-open"
        aria-expanded={open}
        onClick={() => {
          setFailed(null);
          setOpen(!open);
        }}
      >
        <svg
          width="14"
          height="14"
          viewBox="0 0 24 24"
          fill="none"
          aria-hidden="true"
        >
          <path
            d="M9 6v12M15 6v12"
            stroke="currentColor"
            strokeWidth="2.2"
            strokeLinecap="round"
          />
        </svg>
        Pause processing
      </button>
      {announcement ? (
        <span
          className="sr-only"
          role="status"
          data-testid="pause-announcement"
        >
          {announcement}
        </span>
      ) : null}
      {open ? (
        <div className="mm-pause-menu" data-testid="pause-menu">
          <p className="mm-pause-menu__heading">Pause for how long?</p>
          {DURATIONS.map((d) => (
            <button
              key={d.label}
              type="button"
              className="mm-head-control"
              data-testid={`pause-for-${d.minutes ?? "indefinite"}`}
              disabled={save.isPending}
              onClick={() =>
                attempt(
                  "pause",
                  {
                    paused: true,
                    pause_for_minutes: d.minutes,
                    scan_while_paused: state.scan_while_paused,
                  },
                  () => {
                    setOpen(false);
                    setAnnouncement(pausedAnnouncement(d));
                  },
                )
              }
            >
              {d.label}
            </button>
          ))}
          <label className="mm-pause-scan-toggle">
            <input
              type="checkbox"
              data-testid="pause-scan-while-paused"
              checked={state.scan_while_paused}
              onChange={(e) =>
                attempt("save", {
                  paused: state.paused,
                  scan_while_paused: e.target.checked,
                })
              }
            />
            Keep looking for new files while paused
          </label>
          {/* Said out loud, because the assumption otherwise is that work stops dead. */}
          <p className="mm-pause-policy" data-testid="pause-policy">
            {state.in_flight_policy}
          </p>
          {failed ? (
            <p
              className="mm-pause-alert mm-status-text--failed"
              role="alert"
              data-testid="pause-alert"
            >
              {FAILURE_TEXT[failed]}
            </p>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
