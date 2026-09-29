import type { CSSProperties } from "react";

import {
  FLOW_STEPS,
  linkState,
  stepStates,
  type FlowFailure,
  type FlowPosition,
  type LinkState,
  type StepState,
} from "./stage-flow-model";

/** The thinnest the line's fill gets, so a pass that has just started writing still shows. */
const MIN_FILL_PERCENT = 2;

/** What a screen reader hears after each step's name; the node's shape says the same to everyone else. */
const STATE_WORDS: Record<StepState, string> = {
  done: "done",
  now: "in progress",
  next: "up next",
  failed: "failed",
};

function Node({ state }: { state: StepState }) {
  return (
    <span className="mm-flow__node" aria-hidden="true">
      {state === "done" ? (
        <svg viewBox="0 0 12 12" className="mm-flow__glyph">
          <path d="M2.5 6.5 5 9l4.5-5.5" />
        </svg>
      ) : null}
      {state === "failed" ? (
        <svg viewBox="0 0 12 12" className="mm-flow__glyph">
          <path d="M3 3l6 6M9 3l-6 6" />
        </svg>
      ) : null}
      {state === "now" ? <i className="mm-flow__dot" /> : null}
    </span>
  );
}

/** The line from the step before this one to it. During Write its fill is the pass's own percent. */
function Link({ link, percent }: { link: LinkState; percent: number | null }) {
  const live = link === "live";
  return (
    <span
      className={`mm-flow__link mm-flow__link--${link}`}
      role={live ? "progressbar" : undefined}
      aria-label={live ? "Write progress" : undefined}
      aria-valuemin={live ? 0 : undefined}
      aria-valuemax={live ? 100 : undefined}
      aria-valuenow={live ? Math.round(percent ?? 0) : undefined}
      aria-hidden={live ? undefined : true}
    >
      <span
        className="mm-flow__fill"
        style={
          live
            ? ({
                "--mm-live-fill": Math.max(MIN_FILL_PERCENT, percent ?? 0),
              } as CSSProperties)
            : undefined
        }
      />
    </span>
  );
}

/**
 * A file's stages as one connected path, left to right: finished steps ticked, the current one moving,
 * the rest muted. It is an ordered list, so a screen reader hears the order and `aria-current` names the
 * step the file is on; every step keeps its text label, so nothing rests on colour alone.
 */
export function StageFlow({
  position,
  percent = null,
  failure = null,
}: {
  position: FlowPosition;
  /** The pass's live percent, drawn along the line into Write. */
  percent?: number | null;
  /** Where and why the file stopped, drawn on the step it stopped at. */
  failure?: FlowFailure | null;
}) {
  const states = stepStates(position, failure);
  return (
    <div className="mm-flow" data-testid="live-stage-flow">
      <ol className="mm-flow__steps" aria-label="Stages">
        {FLOW_STEPS.map((step, index) => {
          const state = states[index];
          return (
            <li
              key={step.id}
              className={`mm-flow__step mm-flow__step--${state}`}
              aria-current={state === "now" ? "step" : undefined}
            >
              {index > 0 ? (
                <Link
                  link={linkState(state, step.id, percent)}
                  percent={percent}
                />
              ) : null}
              <Node state={state} />
              <span className="mm-flow__label">
                {step.label}
                <span className="sr-only"> ({STATE_WORDS[state]})</span>
              </span>
            </li>
          );
        })}
      </ol>
      {failure ? (
        <p className="mm-flow__reason" role="status">
          {failure.reason}
        </p>
      ) : null}
    </div>
  );
}
