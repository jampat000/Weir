import { useState, type ReactNode } from "react";

import { HeaderSearch } from "../../../../components/shell/header-search";
import { ShellHeaderSlot } from "../../../../components/shell/shell-header-context";
import { useTitleLineFit } from "../../../../components/shell/title-line-fit";
import type {
  SystemLogLevel,
  SystemLogPage,
  SystemLogSource,
} from "../../../../lib/system/system-log-api";
import { LogChips, type LogChip } from "./log-chips";
import { LOG_LEVEL_CHIPS, LOG_SOURCES, type LogFilters } from "./log-filters";
import { LogPickers } from "./log-pickers";

/** What gives way, in order, when the header's line is short of room: the search shrinks to a mark, the pickers leave for the Log card, then the level chips. */
const SEARCH_AS_MARK = 1;
const PICKERS_IN_CARD = 2;
const LEVELS_IN_CARD = 3;

/** The filters the header had no room for, for the Log card to show; null for each the header still holds. */
export type CardFilters = { pickers: ReactNode; levels: ReactNode };

type Counts = SystemLogPage["counts"] | undefined;

/** The source chips: All, then each source on its own, with how many rows choosing it shows. Choosing every source is All. */
function sourceChips(filters: LogFilters, counts: Counts): LogChip[] {
  const all = counts
    ? Object.values(counts.source).reduce((sum, n) => sum + n, 0)
    : undefined;
  return [
    {
      value: "all",
      label: "All",
      count: all,
      pressed: filters.sources.length === 0,
    },
    ...LOG_SOURCES.map((source) => ({
      value: source.value,
      label: source.label,
      count: counts?.source[source.value],
      pressed: filters.sources.includes(source.value),
    })),
  ];
}

function levelChips(filters: LogFilters, counts: Counts): LogChip[] {
  return LOG_LEVEL_CHIPS.map((chip) => ({
    value: chip.value,
    label: chip.label,
    level: chip.value,
    count: counts
      ? chip.levels.reduce((sum, level) => sum + counts.level[level], 0)
      : undefined,
    pressed: chip.levels.every((level) => filters.levels.includes(level)),
  }));
}

/** Choosing every source is the same as choosing none: All. */
function nextSources(
  sources: readonly SystemLogSource[],
  value: SystemLogSource,
): SystemLogSource[] {
  const next = sources.includes(value)
    ? sources.filter((source) => source !== value)
    : [...sources, value];
  return next.length === LOG_SOURCES.length ? [] : next;
}

/** A level chip stands for the levels it names: pressing it adds or removes them together. */
function nextLevels(
  levels: readonly SystemLogLevel[],
  chipValue: string,
): SystemLogLevel[] {
  const chip = LOG_LEVEL_CHIPS.find((c) => c.value === chipValue);
  if (!chip) return [...levels];
  return chip.levels.every((level) => levels.includes(level))
    ? levels.filter((level) => !chip.levels.includes(level))
    : [...new Set([...levels, ...chip.levels])];
}

/**
 * Logs' filters. The header's title line stays one line, as every page's is: it holds the search, the source chips and the
 * level chips, then the category, workflow and time pickers, as much of that as the room allows. What does not fit goes to
 * the Log card, which this hands to `children` as `moved`, so no filter is lost and nothing wraps or scrolls. Each
 * applies as it is chosen, the search once typing pauses.
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
  /** The Log card, given the filters that did not fit the header. */
  children: (moved: CardFilters) => ReactNode;
}) {
  const chosen = [...filters.sources, ...filters.levels].join(",");
  const [chipsRow, setChipsRow] = useState<HTMLDivElement | null>(null);
  const { stage } = useTitleLineFit({
    row: chipsRow,
    stages: LEVELS_IN_CARD,
    refit: `${chosen}|${search !== ""}|${JSON.stringify(counts)}`,
  });
  const pickers = (
    <LogPickers filters={filters} counts={counts} onChange={onChange} />
  );
  const levels = (
    <LogChips
      ariaLabel="Level"
      dataTestId="logs-level-chips"
      chips={levelChips(filters, counts)}
      onToggle={(value) =>
        onChange({ levels: nextLevels(filters.levels, value) })
      }
    />
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
            <LogChips
              ariaLabel="Source"
              dataTestId="logs-source-chips"
              chips={sourceChips(filters, counts)}
              onToggle={(value) =>
                onChange({
                  sources:
                    value === "all"
                      ? []
                      : nextSources(filters.sources, value as SystemLogSource),
                })
              }
            />
            {stage < LEVELS_IN_CARD ? levels : null}
          </div>
          {stage < PICKERS_IN_CARD ? (
            <div className="mm-history-scope">{pickers}</div>
          ) : null}
        </div>
      </ShellHeaderSlot>
      {children({
        pickers: stage >= PICKERS_IN_CARD ? pickers : null,
        levels: stage >= LEVELS_IN_CARD ? levels : null,
      })}
    </>
  );
}
