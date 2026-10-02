import type { ProcessingFile } from "../../../lib/processing/files-api";
import type { LeavingCard } from "../leaving-cards";
import type { Lanes, WorkSource } from "../processing-model";
import type { Filter } from "../processing-filter";
import {
  arrivingWorkflow,
  deliveredWords,
  deliveringWords,
  incomingWords,
  queuedWords,
  stoppedWords,
  workingWords,
} from "./card-words";
import type { CardEnd, CardWords, PipelineCard } from "./pipeline-card-types";
import {
  PIPELINE_STAGES,
  stageOfStep,
  type PipelineStage,
} from "./pipeline-stages";

/** Whether a file of this source is on the board for the page's filter. */
export function shownBy(filter: Filter, item: { source: WorkSource }): boolean {
  return filter === "all" || filter === item.source;
}

/** What every kind of item in the lanes says about itself that the card needs to be placed. */
type Located = {
  key: string;
  name: string;
  path: string;
  libraryName: string;
  source: WorkSource;
  file: ProcessingFile | null;
};

function card(
  item: Located,
  stage: PipelineStage,
  words: CardWords,
  end: CardEnd | null = null,
): PipelineCard {
  return {
    key: item.key,
    stage,
    source: item.source,
    title: item.name,
    path: item.path,
    workflow: item.libraryName,
    file: item.file,
    end,
    ...words,
  };
}

function endedCard(ended: LeavingCard): PipelineCard {
  const { outcome } = ended;
  if (outcome.kind === "done") {
    return card(
      ended,
      "delivering",
      deliveredWords(ended.source, ended.libraryName, ended.path),
      "delivered",
    );
  }
  return card(
    ended,
    stageOfStep(outcome.at),
    stoppedWords(
      outcome.kind,
      outcome.reason,
      ended.source,
      ended.libraryName,
      ended.path,
    ),
    outcome.kind,
  );
}

/**
 * The cards of the Pipeline: one per file or library clean in the lanes the page filter lets through, then
 * the cards of files that have just ended. A file that is back on the board has only its live card.
 */
export function buildPipelineCards(
  lanes: Lanes,
  leaving: readonly LeavingCard[],
  filter: Filter,
  now: number,
): PipelineCard[] {
  const cards: PipelineCard[] = [];
  if (filter !== "library") {
    for (const item of lanes.arriving) {
      const located = {
        ...item,
        source: "download",
        libraryName: arrivingWorkflow(item),
      } as const;
      cards.push(card(located, "incoming", incomingWords(item, now)));
    }
  }
  lanes.waiting
    .filter((item) => shownBy(filter, item))
    .forEach((item, index) => {
      cards.push(card(item, "queued", queuedWords(item, index + 1)));
    });
  for (const item of lanes.working.filter((w) => shownBy(filter, w))) {
    const stage = stageOfStep(item.step);
    cards.push(card(item, stage, workingWords(item, stage)));
  }
  for (const item of lanes.handing.filter((h) => shownBy(filter, h))) {
    cards.push(card(item, "delivering", deliveringWords(item)));
  }
  const live = new Set(cards.map((entry) => entry.key));
  for (const entry of leaving) {
    if (shownBy(filter, entry) && !live.has(entry.key)) {
      cards.push(endedCard(entry));
    }
  }
  return cards;
}

/** The cards at each station, the ones that have just ended first, so they are never pushed below the rows that show. */
export function groupByStage(
  cards: readonly PipelineCard[],
): Record<PipelineStage, PipelineCard[]> {
  const grouped = Object.fromEntries(
    PIPELINE_STAGES.map((stage) => [stage, [] as PipelineCard[]]),
  ) as Record<PipelineStage, PipelineCard[]>;
  for (const entry of cards) grouped[entry.stage].push(entry);
  for (const stage of PIPELINE_STAGES) {
    grouped[stage].sort(
      (a, b) => Number(b.end !== null) - Number(a.end !== null),
    );
  }
  return grouped;
}
