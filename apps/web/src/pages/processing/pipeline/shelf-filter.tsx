import { Chip } from "../../../components/panels/chip";

/** One chip: a workflow, or null for all of them. */
export type ShelfFilterChoice = { id: number | null; name: string };

/** The workflows to narrow the shelf to, as a row of toggle buttons that each say whether they are pressed. */
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
  return (
    <div className="mm-shelf__filter" role="group" aria-label="Show workflow">
      {choices.map((choice) => {
        const pressed = choice.id === chosen;
        return (
          <button
            key={choice.id ?? "all"}
            type="button"
            className="mm-shelf__chip"
            aria-pressed={pressed}
            onClick={() => onChoose(choice.id)}
          >
            <Chip dot={false} tone={pressed ? "info" : "neutral"}>
              {choice.name}
            </Chip>
          </button>
        );
      })}
    </div>
  );
}
