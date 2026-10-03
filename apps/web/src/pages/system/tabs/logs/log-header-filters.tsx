import { useState, type ReactNode } from "react";

import {
  SegmentedControl,
  type SegmentedOption,
} from "../../../../components/panels/segmented-control";
import { HeaderSearch } from "../../../../components/shell/header-search";
import { ShellHeaderSlot } from "../../../../components/shell/shell-header-context";
import { useTitleLineFit } from "../../../../components/shell/title-line-fit";
import type {
  SystemLogPage,
  SystemLogSource,
} from "../../../../lib/system/system-log-api";
import { LOG_SOURCES, type LogFilters } from "./log-filters";
import { LogPickers } from "./log-pickers";

/** What gives way, in order, when the header's line is short of room: the search shrinks to a mark, then the pickers leave for the Log card. */
const SEARCH_AS_MARK = 1;
const PICKERS_IN_CARD = 2;

type Counts = SystemLogPage["counts"] | undefined;

type SourceChoice = "all" | SystemLogSource;

/** The source chips: All, then each source on its own, with how many rows choosing it shows. One is chosen, as on Activity. */
function sourceOptions(counts: Counts): SegmentedOption<SourceChoice>[] {
  const all = counts
    ? Object.values(counts.source).reduce((sum, n) => sum + n, 0)
    : undefined;
  const choices: { value: SourceChoice; label: string; count?: number }[] = [
    { value: "all", label: "All", count: all },
    ...LOG_SOURCES.map((source) => ({
      ...source,
      count: counts?.source[source.value],
    })),
  ];
  return choices.map(({ value, label, count }) => ({
    value,
    label:
      count === undefined ? (
        label
      ) : (
        <>
          {label}{" "}
          <span className="mm-segmented__count">{count.toLocaleString()}</span>
        </>
      ),
  }));
}

/**
 * Logs' filters. The header's title line stays one line, as every page's is: it holds the search and the source chips,
 * then the category, level, workflow and time pickers, as much of that as the room allows. The pickers that do not fit go
 * to the Log card, which this hands to `children` as `movedPickers`, so no filter is lost and nothing wraps or scrolls.
 * Each applies as it is chosen, the search once typing pauses.
 */
export function LogHeaderFilters({
  filters,
  counts,
  search,
  onSearch,
  onChange,
  children,
}: {
  filters: LogFilters;
  /** What each choice would show, from the newest reading; undefined until there is one. */
  counts: Counts;
  /** What is typed in the search box, before it is applied. */
  search: string;
  onSearch: (next: string) => void;
  onChange: (next: Partial<LogFilters>) => void;
  /** The Log card, given the pickers when they did not fit the header (null while the header holds them). */
  children: (movedPickers: ReactNode) => ReactNode;
}) {
  const chosen: SourceChoice = filters.source ?? "all";
  const [chipsRow, setChipsRow] = useState<HTMLDivElement | null>(null);
  const { stage } = useTitleLineFit({
    row: chipsRow,
    stages: PICKERS_IN_CARD,
    refit: `${chosen}|${search !== ""}|${JSON.stringify(counts)}`,
  });
  const pickers = (
    <LogPickers filters={filters} counts={counts} onChange={onChange} />
  );

  return (
    <>
      <ShellHeaderSlot>
        <div
          className="mm-history-controls mm-logs-controls"
          data-testid="logs-controls"
        >
          <HeaderSearch
            label="Search the log"
            placeholder="Search the log"
            className="mm-history-search-box"
            collapsed={stage >= SEARCH_AS_MARK}
            value={search}
            onChange={(event) => onSearch(event.target.value)}
          />
          <div className="mm-history-chips" ref={setChipsRow}>
            <SegmentedControl
              ariaLabel="Source"
              dataTestId="logs-source-chips"
              value={chosen}
              options={sourceOptions(counts)}
              onChange={(next) => {
                if (next !== chosen)
                  onChange({ source: next === "all" ? null : next });
              }}
            />
          </div>
          {stage < PICKERS_IN_CARD ? (
            <div className="mm-history-scope">{pickers}</div>
          ) : null}
        </div>
      </ShellHeaderSlot>
      {children(stage >= PICKERS_IN_CARD ? pickers : null)}
    </>
  );
}
