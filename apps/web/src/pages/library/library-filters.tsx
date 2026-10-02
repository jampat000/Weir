import type {
  LibraryFileClassification,
  LibraryOverview,
  LibraryTotals,
  LibraryProblemKind,
} from "../../lib/processing/library-mode-api";
import { useChipRow } from "../../lib/ui/use-chip-row";
import { PROBLEM_LABELS, type Rag } from "./library-model";

export type LibraryState = "cleaned" | "left_alone";

/** Which slice of the library the table shows. A chip picks one; the reason list narrows "will not touch". */
export type LibraryFilter = {
  classification: LibraryFileClassification | null;
  problem: LibraryProblemKind | null;
  state: LibraryState | null;
};

export const NO_FILTER: LibraryFilter = {
  classification: null,
  problem: null,
  state: null,
};

type Chip = {
  id: string;
  label: string;
  hint: string;
  /** Its colour, which is the same as the colour of the same thing in the table. Without one it is plain. */
  rag?: Rag;
  count: (totals: LibraryTotals) => number;
  selected: (filter: LibraryFilter) => boolean;
  /** The filter a click on this chip asks for. */
  choose: (filter: LibraryFilter) => LibraryFilter;
};

function byClassification(
  id: LibraryFileClassification,
  label: string,
  hint: string,
  rag: Rag,
  count: Chip["count"],
): Chip {
  return {
    id,
    label,
    hint,
    rag,
    count,
    selected: (filter) => filter.classification === id,
    choose: (filter) => ({
      ...NO_FILTER,
      classification: filter.classification === id ? null : id,
    }),
  };
}

function byState(
  id: LibraryState,
  label: string,
  hint: string,
  rag: Rag,
  count: Chip["count"],
): Chip {
  return {
    id,
    label,
    hint,
    rag,
    count,
    selected: (filter) => filter.state === id,
    choose: (filter) => ({
      ...NO_FILTER,
      state: filter.state === id ? null : id,
    }),
  };
}

/**
 * The chips, in the order an operator reads them: every file, then the counts by what the rules say about a file, then
 * by what Weir has done with it. Each count that is also a filter has the colour of its files in the table.
 */
const CHIPS: Chip[] = [
  {
    id: "all",
    label: "All",
    hint: "Every file in this workflow",
    count: (totals) => totals.files,
    selected: (filter) =>
      !filter.classification && !filter.state && !filter.problem,
    choose: () => NO_FILTER,
  },
  byClassification(
    "would_change",
    "Would change",
    "Would change: Weir would take tracks out of these",
    "bad",
    (totals) => totals.would_change,
  ),
  byClassification(
    "matches",
    "Matches rules",
    "Matches your rules: nothing to do, these already look the way your rules ask",
    "good",
    (totals) => totals.matches,
  ),
  byClassification(
    "cannot_process",
    "Untouched",
    "Weir will not touch these: still seeding, or Weir cannot read them",
    "muted",
    (totals) => totals.cannot_process,
  ),
  byState(
    "cleaned",
    "Cleaned",
    "Cleaned: Weir has cleaned these at least once; a rescan does not forget",
    "good",
    (totals) => totals.cleaned,
  ),
  byState(
    "left_alone",
    "Left alone",
    "Left alone: you marked these; nothing cleans them until you clear it",
    "muted",
    (totals) => totals.left_alone,
  ),
];

const UNKNOWN_COUNT = "—";

function ProblemSelect({
  overview,
  filter,
  onFilter,
}: {
  overview: LibraryOverview | undefined;
  filter: LibraryFilter;
  onFilter: (filter: LibraryFilter) => void;
}) {
  const problems = overview?.problems ?? [];
  if (problems.length === 0) return null;
  return (
    <label className="mm-library-problem">
      <span className="sr-only">Why Weir will not touch a file</span>
      <select
        className="mm-input"
        value={filter.problem ?? ""}
        onChange={(event) => {
          const value = event.target.value as LibraryProblemKind | "";
          onFilter({
            ...filter,
            problem: value === "" ? null : value,
            classification: null,
          });
        }}
      >
        <option value="">Any reason</option>
        {problems.map((group) => (
          <option key={group.kind} value={group.kind}>
            {PROBLEM_LABELS[group.kind]} ({group.files.toLocaleString()})
          </option>
        ))}
      </select>
    </label>
  );
}

/**
 * What narrows the Files table: the search, then the counts that are also the filters. They sit on the header's title
 * line after the library picker (see LibraryHeaderControls), where the search gives up room first, then the chips
 * scroll sideways inside their own box if they still do not fit.
 */
export function LibraryFilters({
  search,
  onSearch,
  overview,
  filter,
  onFilter,
}: {
  search: string;
  onSearch: (value: string) => void;
  overview: LibraryOverview | undefined;
  filter: LibraryFilter;
  onFilter: (filter: LibraryFilter) => void;
}) {
  const totals = overview?.totals;
  const { setRow, scrolls } = useChipRow(
    filter.classification ?? filter.state ?? "all",
  );
  return (
    <>
      <input
        type="search"
        className="mm-input mm-library-search"
        placeholder="Search"
        aria-label="Search this workflow"
        value={search}
        onChange={(event) => onSearch(event.target.value)}
      />
      <div className="mm-library-chips-box" data-scrolls={scrolls} ref={setRow}>
        <div className="mm-library-chips" role="group" aria-label="Show">
          {CHIPS.map((chip) => {
            const count = totals ? chip.count(totals) : null;
            return (
              <button
                key={chip.id}
                type="button"
                className="mm-library-chip"
                data-rag={chip.rag}
                data-empty={count === 0 ? "" : undefined}
                title={chip.hint}
                aria-pressed={chip.selected(filter)}
                onClick={() => onFilter(chip.choose(filter))}
              >
                {chip.label}{" "}
                <b>{count === null ? UNKNOWN_COUNT : count.toLocaleString()}</b>
              </button>
            );
          })}
          <ProblemSelect
            overview={overview}
            filter={filter}
            onFilter={onFilter}
          />
        </div>
      </div>
    </>
  );
}
