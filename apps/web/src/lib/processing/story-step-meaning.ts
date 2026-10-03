import type { StatusMeaning } from "../ui/status-meaning";
import type { ProcessingFileStoryStep } from "./files-api";

/** What a step of a file's story means: it went well, needs a look, went wrong, or only says what happened. */
export const STORY_STEP_MEANING: Record<
  ProcessingFileStoryStep["tone"],
  StatusMeaning
> = {
  good: "done",
  warn: "attention",
  bad: "broken",
  neutral: "idle",
};
