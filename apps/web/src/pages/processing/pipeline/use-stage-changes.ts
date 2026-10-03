import { useState } from "react";

import type { PipelineCard } from "./pipeline-card-types";
import type { PipelineStage } from "./pipeline-stages";

type Stages = ReadonlyMap<string, PipelineStage>;

const NONE: ReadonlySet<string> = new Set();

function stagesOf(cards: readonly PipelineCard[]): Stages {
  return new Map(cards.map((card) => [card.key, card.stage]));
}

function sameStages(a: Stages, b: Stages): boolean {
  if (a.size !== b.size) return false;
  for (const [key, stage] of b) if (a.get(key) !== stage) return false;
  return true;
}

/**
 * The keys of the cards that have moved to another station since they were first seen: they glow as they
 * arrive. A card that is new to the board has not moved, and one that has left is forgotten.
 */
export function useStageChanges(
  cards: readonly PipelineCard[],
): ReadonlySet<string> {
  const [state, setState] = useState<{
    stages: Stages;
    moved: ReadonlySet<string>;
  }>(() => ({ stages: stagesOf(cards), moved: NONE }));
  const stages = stagesOf(cards);
  if (!sameStages(state.stages, stages)) {
    const moved = new Set<string>();
    for (const [key, stage] of stages) {
      const before = state.stages.get(key);
      if (before !== undefined && before !== stage) moved.add(key);
      else if (state.moved.has(key)) moved.add(key);
    }
    setState({ stages, moved });
  }
  return state.moved;
}
