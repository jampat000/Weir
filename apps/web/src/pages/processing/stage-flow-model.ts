/**
 * The stages a file passes through, in order, and how each one reads at a given moment. Pure functions,
 * so the rules are tested on their own; the component (stage-flow.tsx) only draws what they say.
 */

export const FLOW_STEPS = [
  { id: "checking", label: "Checking" },
  { id: "plan", label: "Plan" },
  { id: "write", label: "Write" },
  { id: "verify", label: "Verify" },
  { id: "hand-back", label: "Hand back" },
] as const;

export type FlowStepId = (typeof FLOW_STEPS)[number]["id"];

/** Every step is finished: the moment before the file moves to Just finished. */
export const FLOW_DONE = "done";

export type FlowPosition = FlowStepId | typeof FLOW_DONE;

export type FlowFailure = { at: FlowStepId; reason: string };

export type StepState = "done" | "now" | "next" | "failed";

/** How the line joining a step to the one before it looks. */
export type LinkState = "full" | "empty" | "busy" | "live";

/** The names the server gives each stage in its live progress (`progress_stage`). */
const STEP_BY_STAGE: Record<string, FlowStepId> = {
  checking: "checking",
  planning: "plan",
  writing: "write",
  verifying: "verify",
  handing_back: "hand-back",
};

/**
 * The step a pass is on: the one the server names, or `inferred` when it names none (a server that predates
 * `progress_stage`, or a report from before the pass has said anything).
 */
export function stepForStage(
  stage: string | null | undefined,
  inferred: FlowStepId,
): FlowStepId {
  return (stage ? STEP_BY_STAGE[stage] : undefined) ?? inferred;
}

export function stepIndex(id: FlowStepId): number {
  return FLOW_STEPS.findIndex((step) => step.id === id);
}

/** The state of each step, in order. A failure marks its step, and every step before it is done. */
export function stepStates(
  position: FlowPosition,
  failure: FlowFailure | null,
): StepState[] {
  if (failure) {
    const failedAt = stepIndex(failure.at);
    return FLOW_STEPS.map((_, index) =>
      index < failedAt ? "done" : index === failedAt ? "failed" : "next",
    );
  }
  const current =
    position === FLOW_DONE ? FLOW_STEPS.length : stepIndex(position);
  return FLOW_STEPS.map((_, index) =>
    index < current ? "done" : index === current ? "now" : "next",
  );
}

/**
 * The line into a step is full once the step is reached, empty before, and while the step is the current
 * one it moves: with the pass's own percent during Write, and as an unmeasured sweep during the others.
 */
export function linkState(
  step: StepState,
  id: FlowStepId,
  percent: number | null,
): LinkState {
  if (step === "done" || step === "failed") return "full";
  if (step === "next") return "empty";
  return id === "write" && percent != null ? "live" : "busy";
}
