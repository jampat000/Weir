import { formatBytes } from "../../lib/format/bytes";
import type {
  LibraryFileClassification,
  LibraryOverview,
  LibraryTotals,
  LibraryProblemKind,
} from "../../lib/processing/library-mode-api";
import { PROBLEM_LABELS } from "./library-model";

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

/** The chips: a count and the filter it stands for, in the order an operator reads them. */
const CHIPS: {
  id: LibraryFileClassification;
  label: string;
  hint: string;
  count: (totals: LibraryTotals) => number;
}[] = [
  {
    id: "would_change",
    label: "Would change",
    hint: "Weir would take tracks out of these",
    count: (totals) => totals.would_change,
  },
  {
    id: "matches",
    label: "Matches your rules",
    hint: "Nothing to do: these already look the way your rules ask",
    count: (totals) => totals.matches,
  },
  {
    id: "cannot_process",
    label: "Weir will not touch",
    hint: "Still seeding, or Weir cannot read them",
    count: (totals) => totals.cannot_process,
  },
];

/** The same row of chips, for what Weir has done with a file rather than what is in it. */
const STATE_CHIPS: {
  id: LibraryState;
  label: string;
  hint: string;
  count: (totals: LibraryTotals) => number;
}[] = [
  {
    id: "cleaned",
    label: "Cleaned",
    hint: "Weir has cleaned these at least once; a rescan does not forget",
    count: (totals) => totals.cleaned,
  },
  {
    id: "left_alone",
    label: "Left alone",
    hint: "You marked these Left alone; nothing cleans them until you clear it",
    count: (totals) => totals.left_alone,
  },
];

const UNKNOWN_COUNT = "—";

/**
 * What cleaning would win back. Never claims there is nothing to reclaim when Weir simply could not
 * measure it: some files carry no per-track size for the scan to add up.
 */
function savingLine(totals: LibraryTotals | undefined): string {
  if (totals && totals.estimated_bytes_saved > 0) {
    return `About ${formatBytes(totals.estimated_bytes_saved)} back if everything that would change is cleaned`;
  }
  if (totals && totals.would_change > 0) {
    const tracks =
      totals.total_removed_audio_tracks + totals.total_removed_subtitle_tracks;
    return `${tracks} tracks would come out; these files do not say how big each one is`;
  }
  return "Nothing to reclaim here at the moment";
}

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

export function LibraryToolbar({
  search,
  onSearch,
  overview,
  filter,
  onFilter,
  compact,
  onCompact,
}: {
  search: string;
  onSearch: (value: string) => void;
  overview: LibraryOverview | undefined;
  filter: LibraryFilter;
  onFilter: (filter: LibraryFilter) => void;
  compact: boolean;
  onCompact: (compact: boolean) => void;
}) {
  const totals = overview?.totals;
  return (
    <div className="mm-library-toolbar">
      <input
        type="search"
        className="mm-input mm-library-search"
        placeholder="Search this library"
        aria-label="Search this library"
        value={search}
        onChange={(event) => onSearch(event.target.value)}
      />
      <div className="mm-library-chips" role="group" aria-label="Show">
        {CHIPS.map((chip) => (
          <button
            key={chip.id}
            type="button"
            className="mm-library-chip"
            title={chip.hint}
            aria-pressed={filter.classification === chip.id}
            onClick={() =>
              onFilter({
                ...NO_FILTER,
                classification:
                  filter.classification === chip.id ? null : chip.id,
              })
            }
          >
            {chip.label}{" "}
            <b>
              {totals ? chip.count(totals).toLocaleString() : UNKNOWN_COUNT}
            </b>
          </button>
        ))}
        {STATE_CHIPS.map((chip) => (
          <button
            key={chip.id}
            type="button"
            className="mm-library-chip"
            title={chip.hint}
            aria-pressed={filter.state === chip.id}
            onClick={() =>
              onFilter({
                ...NO_FILTER,
                state: filter.state === chip.id ? null : chip.id,
              })
            }
          >
            {chip.label}{" "}
            <b>
              {totals ? chip.count(totals).toLocaleString() : UNKNOWN_COUNT}
            </b>
          </button>
        ))}
        <ProblemSelect
          overview={overview}
          filter={filter}
          onFilter={onFilter}
        />
      </div>
      <button
        type="button"
        className="mm-library-chip"
        aria-pressed={compact}
        title="Fit more files on the screen"
        onClick={() => onCompact(!compact)}
      >
        Compact rows
      </button>
      <p className="mm-library-saving">{savingLine(totals)}</p>
    </div>
  );
}
