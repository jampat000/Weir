import { useId } from "react";

import { MmListboxPicker } from "../../../../components/ui/mm-listbox-picker";
import { MmMultiListboxPicker } from "../../../../components/ui/mm-multi-listbox-picker";
import { useProcessingLibrariesQuery } from "../../../../lib/processing/libraries-queries";
import type {
  SystemLogCategory,
  SystemLogPage,
} from "../../../../lib/system/system-log-api";
import {
  LOG_CATEGORY_LABELS,
  LOG_LEVEL_CHOICES,
  LOG_WHEN_OPTIONS,
  levelChoicesFor,
  levelsForChoices,
  type LogFilters,
  type LogWhen,
} from "./log-filters";

const CATEGORY_IDS = Object.keys(LOG_CATEGORY_LABELS) as SystemLogCategory[];

/** What the closed category picker says: nothing chosen is every category, one is named, several are counted. */
function categorySummary(chosen: readonly SystemLogCategory[]): string {
  if (chosen.length === 0) return "All categories";
  if (chosen.length === 1) return LOG_CATEGORY_LABELS[chosen[0]];
  return `${chosen.length} categories`;
}

/** What the closed level picker says: nothing chosen is every level, one is named, several are counted. */
function levelSummary(chosen: readonly string[]): string {
  if (chosen.length === 0) return "All levels";
  if (chosen.length === 1) {
    return (
      LOG_LEVEL_CHOICES.find((choice) => choice.value === chosen[0])?.label ??
      chosen[0]
    );
  }
  return `${chosen.length} levels`;
}

/**
 * The four pickers: what a row is about and how serious it is (each several at once, with how many rows each choice has),
 * its workflow, and how far back to read. They sit on the header's title line when it has room for them, and at the top of the Log card when it has not.
 */
export function LogPickers({
  filters,
  counts,
  onChange,
}: {
  filters: LogFilters;
  /** What each choice would show, from the newest reading; undefined until there is one. */
  counts: SystemLogPage["counts"] | undefined;
  onChange: (next: Partial<LogFilters>) => void;
}) {
  const libraries = useProcessingLibrariesQuery();
  const categoryLabel = useId();
  const levelLabel = useId();
  const workflowLabel = useId();
  const whenLabel = useId();
  const categoryOptions = CATEGORY_IDS.map((id) => ({
    value: id,
    label:
      counts === undefined
        ? LOG_CATEGORY_LABELS[id]
        : `${LOG_CATEGORY_LABELS[id]} · ${counts.category[id].toLocaleString()}`,
  }));
  const chosenLevels = levelChoicesFor(filters.levels);
  const levelOptions = LOG_LEVEL_CHOICES.map((choice) => ({
    value: choice.value,
    label:
      counts === undefined
        ? choice.label
        : `${choice.label} · ${choice.levels
            .reduce((sum, level) => sum + counts.level[level], 0)
            .toLocaleString()}`,
    marker: (
      <span
        className={`mm-log-dot mm-log-dot--${choice.value}`}
        aria-hidden="true"
      />
    ),
  }));
  return (
    <>
      <div className="mm-workflow-picker">
        <span id={categoryLabel} className="sr-only">
          Category
        </span>
        <MmMultiListboxPicker
          data-testid="logs-category-picker"
          options={categoryOptions}
          values={filters.categories}
          summaryText={categorySummary(filters.categories)}
          onChange={(next) =>
            onChange({ categories: next as SystemLogCategory[] })
          }
          ariaLabelledBy={categoryLabel}
        />
      </div>
      <div className="mm-workflow-picker">
        <span id={levelLabel} className="sr-only">
          Level
        </span>
        <MmMultiListboxPicker
          data-testid="logs-level-picker"
          options={levelOptions}
          values={chosenLevels}
          summaryText={levelSummary(chosenLevels)}
          onChange={(next) => onChange({ levels: levelsForChoices(next) })}
          ariaLabelledBy={levelLabel}
        />
      </div>
      <div className="mm-workflow-picker">
        <span id={workflowLabel} className="sr-only">
          Workflow
        </span>
        <MmListboxPicker
          data-testid="logs-workflow-picker"
          options={[
            { value: "", label: "All workflows" },
            ...(libraries.data ?? []).map((library) => ({
              value: String(library.id),
              label: library.name,
            })),
          ]}
          value={filters.workflow === null ? "" : String(filters.workflow)}
          onChange={(next) =>
            onChange({ workflow: next === "" ? null : Number(next) })
          }
          ariaLabelledBy={workflowLabel}
        />
      </div>
      <div className="mm-workflow-picker mm-period-picker">
        <span id={whenLabel} className="sr-only">
          How far back
        </span>
        <MmListboxPicker
          data-testid="logs-when-picker"
          options={LOG_WHEN_OPTIONS}
          value={filters.when}
          onChange={(next) => onChange({ when: next as LogWhen })}
          ariaLabelledBy={whenLabel}
        />
      </div>
    </>
  );
}
