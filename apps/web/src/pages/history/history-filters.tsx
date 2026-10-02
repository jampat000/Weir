import { useId, useState } from "react";

import { SegmentedControl } from "../../components/panels/segmented-control";
import { MmListboxPicker } from "../../components/ui/mm-listbox-picker";
import { ShellHeaderSlot } from "../../components/shell/shell-header-context";
import type { ProcessingLibrary } from "../../lib/processing/libraries-api";
import { HISTORY_GROUPS, type HistoryGroup } from "./history-entries";
import { useChipRow } from "../../lib/ui/use-chip-row";

/** How far back History looks, as the server's within_days. */
export const PERIODS: { id: string; label: string; days?: number }[] = [
  { id: "1", label: "Today", days: 1 },
  { id: "7", label: "Last 7 days", days: 7 },
  { id: "30", label: "Last 30 days", days: 30 },
  { id: "all", label: "Everything kept" },
];
export const DEFAULT_PERIOD = PERIODS[1];

/** Every filter lives in the address, so a filtered view survives a reload and can be linked to. */
export type SetParam = (name: string, value: string | null) => void;

/**
 * What narrows the list: the search, the kinds of file with their counts, and the workflow and how far back. They
 * sit on the header's title line, after the title, where the search gives up room first, then the two pickers, and the
 * chips scroll sideways inside their own box if they still do not fit. In a window too narrow for that line they take
 * a row under the title.
 */
export function HistoryFilters({
  query,
  group,
  counts,
  libraryId,
  periodId,
  libraries,
  setParam,
}: {
  query: string;
  group: HistoryGroup;
  counts: Record<HistoryGroup, number>;
  libraryId: number | undefined;
  periodId: string;
  libraries: ProcessingLibrary[];
  setParam: SetParam;
}) {
  const [search, setSearch] = useState(query);
  const { setRow, scrolls } = useChipRow(group);
  const workflowLabel = useId();
  const periodLabel = useId();
  return (
    <ShellHeaderSlot>
      <div className="mm-history-controls" data-testid="history-filters">
        <form
          className="mm-history-search"
          role="search"
          onSubmit={(event) => {
            event.preventDefault();
            setParam("q", search.trim());
          }}
        >
          <input
            className="mm-input"
            type="search"
            aria-label="Find a file"
            placeholder="Find a file"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            onBlur={() => setParam("q", search.trim())}
            onKeyDown={(event) => {
              if (event.key === "Escape") event.currentTarget.blur();
            }}
          />
        </form>
        <div className="mm-history-chips" data-scrolls={scrolls} ref={setRow}>
          <SegmentedControl
            ariaLabel="Show"
            value={group}
            options={HISTORY_GROUPS.map((g) => ({
              value: g.id,
              label: (
                <>
                  {g.label}{" "}
                  <span className="mm-segmented__count">{counts[g.id]}</span>
                </>
              ),
            }))}
            onChange={(next) => setParam("show", next === "all" ? null : next)}
          />
        </div>
        <div className="mm-history-scope">
          <div className="mm-workflow-picker">
            <span id={workflowLabel} className="sr-only">
              Workflow
            </span>
            <MmListboxPicker
              options={[
                { value: "", label: "All workflows" },
                ...libraries.map((library) => ({
                  value: String(library.id),
                  label: library.name,
                })),
              ]}
              value={libraryId === undefined ? "" : String(libraryId)}
              onChange={(next) => setParam("library", next)}
              ariaLabelledBy={workflowLabel}
            />
          </div>
          <div className="mm-workflow-picker mm-period-picker">
            <span id={periodLabel} className="sr-only">
              How far back
            </span>
            <MmListboxPicker
              options={PERIODS.map((p) => ({ value: p.id, label: p.label }))}
              value={periodId}
              onChange={(next) => setParam("within", next)}
              ariaLabelledBy={periodLabel}
            />
          </div>
        </div>
      </div>
    </ShellHeaderSlot>
  );
}
