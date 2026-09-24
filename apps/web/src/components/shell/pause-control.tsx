import { useState } from "react";

import { useCanEdit } from "../../lib/auth/can-edit";
import { useSavePause, usePauseQuery } from "../../lib/pause/pause-queries";

/** Minutes offered for a pause that lifts itself. */
const DURATIONS: { label: string; minutes: number | null }[] = [
  { label: "30 minutes", minutes: 30 },
  { label: "2 hours", minutes: 120 },
  { label: "8 hours", minutes: 480 },
  { label: "Until I resume", minutes: null },
];

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

  const state = pause.data;
  if (!state) return null;

  const resume = () =>
    save.mutate({
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
      </div>
    );
  }

  if (!editable) return null;

  return (
    <div className="mm-pause-control" data-testid="pause-control">
      <button
        type="button"
        className="mm-head-control"
        data-testid="pause-open"
        aria-expanded={open}
        onClick={() => setOpen(!open)}
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
      {open ? (
        <div className="mm-pause-menu" data-testid="pause-menu">
          {DURATIONS.map((d) => (
            <button
              key={d.label}
              type="button"
              className="mm-head-control"
              data-testid={`pause-for-${d.minutes ?? "indefinite"}`}
              disabled={save.isPending}
              onClick={() => {
                save.mutate({
                  paused: true,
                  pause_for_minutes: d.minutes,
                  scan_while_paused: state.scan_while_paused,
                });
                setOpen(false);
              }}
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
                save.mutate({
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
        </div>
      ) : null}
    </div>
  );
}
