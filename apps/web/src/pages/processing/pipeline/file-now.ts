import type { FileNow } from "../../../components/processing/file-now-section";
import type { PipelineCard } from "./pipeline-card-types";
import { STAGE_LABEL } from "./pipeline-stages";

/** What a file's story says about where it is right now: its card's station, status, bar and facts, in words. */
export function fileNowOf(card: PipelineCard): FileNow {
  return {
    stage: STAGE_LABEL[card.stage],
    status: card.status.text,
    working: card.status.pulse || card.bar?.moving === true,
    progress: card.bar && !card.bar.waiting ? card.bar.width : null,
    facts: card.fullFacts,
  };
}
