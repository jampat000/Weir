import { useChipRow } from "../../../lib/ui/use-chip-row";

/** One chip: a workflow, or null for all of them. */
export type ShelfFilterChoice = { id: number | null; name: string };

/**
 * The workflows to narrow the shelf to, as a row of toggle buttons that each say whether they are pressed. The row
 * scrolls sideways when the header has no room for every chip, fading where chips are cut off.
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
  const { setRow, more } = useChipRow(String(chosen ?? "all"));
  return (
    <div
      ref={setRow}
      className="lsh-chips"
      role="group"
      aria-label="Workflows"
      data-more-before={more.before}
      data-more-after={more.after}
    >
      {choices.map((choice) => (
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
      ))}
    </div>
  );
}
