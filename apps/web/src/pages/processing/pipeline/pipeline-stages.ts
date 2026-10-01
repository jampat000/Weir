import type { FlowStepId } from "../stage-flow-model";

/** The five stations a file passes through on the Pipeline, left to right. */
export const PIPELINE_STAGES = [
  "incoming",
  "queued",
  "analysing",
  "processing",
  "delivering",
] as const;

export type PipelineStage = (typeof PIPELINE_STAGES)[number];

/** The stations a running pass is in: the ones its steps map to. */
export type WorkingStage = Extract<
  PipelineStage,
  "analysing" | "processing" | "delivering"
>;

/** Where the files in progress are listed: "and N more" on the Pipeline and on Working on now lead here. */
export const IN_PROGRESS_PATH = "/history?show=working";

export const STAGE_LABEL: Record<PipelineStage, string> = {
  incoming: "Incoming",
  queued: "Queued",
  analysing: "Analysing",
  processing: "Processing",
  delivering: "Delivering",
};

/** The station a pass is in, from the step of its stages it is on. */
export function stageOfStep(step: FlowStepId): WorkingStage {
  switch (step) {
    case "checking":
    case "plan":
      return "analysing";
    case "write":
    case "verify":
      return "processing";
    case "hand-back":
      return "delivering";
  }
}
