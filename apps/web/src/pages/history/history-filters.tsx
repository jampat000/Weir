import { useState } from "react";

import type { ProcessingLibrary } from "../../lib/processing/libraries-api";
import { HISTORY_GROUPS, type HistoryGroup } from "./history-model";

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
  return (
    <div className="mm-history-filters" data-testid="history-filters">
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
        />
      </form>
      <div className="mm-history-chips" role="group" aria-label="Show">
        {HISTORY_GROUPS.map((g) => (
          <button
            key={g.id}
            type="button"
            className="mm-history-chip"
            aria-pressed={g.id === group}
            onClick={() => setParam("show", g.id === "all" ? null : g.id)}
          >
            {g.label}{" "}
            <span className="mm-history-chip__count">{counts[g.id]}</span>
          </button>
        ))}
      </div>
      <div className="mm-history-scope">
        <select
          className="mm-input"
          aria-label="Library"
          value={libraryId ?? ""}
          onChange={(event) => setParam("library", event.target.value)}
        >
          <option value="">All libraries</option>
          {libraries.map((library) => (
            <option key={library.id} value={library.id}>
              {library.name}
            </option>
          ))}
        </select>
        <select
          className="mm-input"
          aria-label="How far back"
          value={periodId}
          onChange={(event) => setParam("within", event.target.value)}
        >
          {PERIODS.map((p) => (
            <option key={p.id} value={p.id}>
              {p.label}
            </option>
          ))}
        </select>
      </div>
    </div>
  );
}
