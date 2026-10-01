/** The words of the Working on now tile: each step a pass can be on, and how long a new download waits. */
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import type { FlowStepId } from "../stage-flow-model";

export const STEP_WORDS: Record<FlowStepId, string> = {
  checking: "Checking",
  plan: "Planning",
  write: "Writing",
  verify: "Verifying",
  "hand-back": "Handing back",
};

/**
 * How many seconds a new download is left alone after it stops changing, when every workflow that is
 * switched on agrees. With none switched on, or with different waits, there is no single answer.
 */
export function sharedWaitSeconds(
  workflows: readonly Pick<
    ProcessingLibrary,
    "enabled" | "ready_after_seconds"
  >[],
): number | null {
  const waits = new Set(
    workflows
      .filter((workflow) => workflow.enabled)
      .map((workflow) => workflow.ready_after_seconds),
  );
  const [wait] = waits;
  return waits.size === 1 && wait > 0 ? wait : null;
}
