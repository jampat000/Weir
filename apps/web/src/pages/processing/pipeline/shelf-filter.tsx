import { useState } from "react";

import { MoreMenu } from "../../../components/shell/more-menu";
import {
  foldedChipIndexes,
  useTitleLineFit,
} from "../../../components/shell/title-line-fit";

/** One chip: a workflow, or null for all of them. */
export type ShelfFilterChoice = { id: number | null; name: string };

/**
 * The workflows to narrow the shelf to, as a row of toggle buttons that each say whether they are pressed. When the
 * header has no room for every chip the last ones fold into a "More" menu: chips are whole or not there, and none runs
 * under the link beside them.
 */
export function ShelfFilter({
  choices,
  chosen,
  onChoose,
}: {
  choices: readonly ShelfFilterChoice[];
  /** The workflow the shelf is narrowed to; none when it shows them all. */
  chosen: number | null;
  onChoose: (workflowId: number | null) => void;
}) {
  const [row, setRow] = useState<HTMLDivElement | null>(null);
  const { folded } = useTitleLineFit({
    row,
    stages: 0,
    chips: choices.length,
    refit: `${chosen}|${choices.map((choice) => choice.name).join("\u0000")}`,
  });
  const hidden = foldedChipIndexes(
    choices.length,
    folded,
    choices.findIndex((choice) => choice.id === chosen),
  );
  return (
    <div ref={setRow} className="lsh-chips" role="group" aria-label="Workflows">
      {choices.map((choice, index) =>
        hidden.has(index) ? null : (
          <button
            key={choice.id ?? "all"}
            type="button"
            className="lsh-chip"
            aria-pressed={choice.id === chosen}
            data-library={choice.id ?? "all"}
            onClick={() => onChoose(choice.id)}
          >
            {choice.name}
          </button>
        ),
      )}
      {hidden.size > 0 ? (
        <MoreMenu
          menuLabel="More workflows"
          folded={choices.flatMap((choice, index) =>
            hidden.has(index)
              ? [{ id: String(choice.id ?? "all"), label: choice.name }]
              : [],
          )}
          onChoose={(id) => onChoose(id === "all" ? null : Number(id))}
          buttonClassName="lsh-chip"
        />
      ) : null}
    </div>
  );
}
