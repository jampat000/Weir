import { useId } from "react";

import { MmListboxPicker } from "../../../components/ui/mm-listbox-picker";
import { FILTER_OPTIONS, type Filter } from "../processing-filter";

type WorkPickerProps = {
  value: Filter;
  onChange: (filter: Filter) => void;
};

/**
 * "Everything ▾", "New downloads ▾" or "Library cleaning ▾": the same choice as the three-button switch, for a header
 * with no room for that. It looks and sizes like the workflow picker beside it.
 */
export function WorkPicker({ value, onChange }: WorkPickerProps) {
  const labelId = useId();
  return (
    <div className="mm-workflow-picker mm-work-picker">
      <span id={labelId} className="sr-only">
        Show work from
      </span>
      <MmListboxPicker
        options={FILTER_OPTIONS.map(({ value: option, label }) => ({
          value: option,
          label: String(label),
        }))}
        value={value}
        onChange={(next) => onChange(next as Filter)}
        ariaLabelledBy={labelId}
        data-testid="live-filter-compact"
      />
    </div>
  );
}
