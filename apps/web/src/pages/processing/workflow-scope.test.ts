import { describe, expect, it } from "vitest";

import { aFile, anEndedCard } from "./pipeline/pipeline-fixtures";
import { leavingInWorkflow } from "./workflow-scope";

function endedIn(id: number, libraryId: number) {
  return anEndedCard(
    id,
    { kind: "done" },
    { file: aFile(id, "processed", { library_id: libraryId }) },
  );
}

describe("leavingInWorkflow", () => {
  const cards = [endedIn(1, 1), endedIn(2, 2), endedIn(3, 1)];

  it("keeps every card when no workflow is chosen", () => {
    expect(leavingInWorkflow(cards, null)).toHaveLength(3);
  });

  it("keeps only the chosen workflow's cards", () => {
    expect(leavingInWorkflow(cards, 1).map((card) => card.file.id)).toEqual([
      1, 3,
    ]);
  });
});
