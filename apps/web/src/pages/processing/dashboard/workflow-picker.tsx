import { useId } from "react";

import { MmListboxPicker } from "../../../components/ui/mm-listbox-picker";

const EVERY_WORKFLOW = "";

type WorkflowPickerProps = {
  /** The workflows that are switched on. With one there is nothing to choose between, so nothing is drawn. */
  workflows: readonly { id: number; name: string }[];
  /** The workflow the page is narrowed to, or null for every workflow. */
  value: number | null;
  onChange: (id: number | null) => void;
};

/** "All workflows" or one workflow by name, for narrowing everything on the Dashboard to it. */
export function WorkflowPicker({
  workflows,
  value,
  onChange,
}: WorkflowPickerProps) {
  const labelId = useId();
  if (workflows.length < 2) return null;
  const options = [
    { value: EVERY_WORKFLOW, label: "All workflows" },
    ...workflows.map((workflow) => ({
      value: String(workflow.id),
      label: workflow.name,
    })),
  ];
  return (
    <div className="mm-workflow-picker">
      <span id={labelId} className="sr-only">
        Show work from workflow
      </span>
      <MmListboxPicker
        options={options}
        value={value === null ? EVERY_WORKFLOW : String(value)}
        onChange={(next) =>
          onChange(next === EVERY_WORKFLOW ? null : Number(next))
        }
        ariaLabelledBy={labelId}
        data-testid="workflow-picker"
      />
    </div>
  );
}
