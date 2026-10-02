import { useId, useState } from "react";
import { createPortal } from "react-dom";

import { SegmentedControl } from "../../components/panels/segmented-control";
import { MmListboxPicker } from "../../components/ui/mm-listbox-picker";
import { ShellHeaderSlot } from "../../components/shell/shell-header-context";
import type { ProcessingLibrary } from "../../lib/processing/libraries-api";
import { ACTIVITY_GROUPS, type ActivityGroup } from "./activity-entries";
import { HeaderSearch } from "../../components/shell/header-search";
import {
  foldedChipIndexes,
  useTitleLineFit,
} from "../../components/shell/title-line-fit";
import { useMediaQuery } from "../../lib/ui/use-media-query";

/** How far back Activity looks, as the server's within_days. */
export const PERIODS: { id: string; label: string; days?: number }[] = [
  { id: "1", label: "Today", days: 1 },
  { id: "7", label: "Last 7 days", days: 7 },
  { id: "30", label: "Last 30 days", days: 30 },
  { id: "all", label: "Everything kept" },
];
export const DEFAULT_PERIOD = PERIODS[1];

/** Every filter lives in the address, so a filtered view survives a reload and can be linked to. */
export type SetParam = (name: string, value: string | null) => void;

/** What gives way first when the title line is short of room: the search shrinks to a mark, then the pickers move into the Files card. */
const SEARCH_AS_MARK = 1;
const PICKERS_IN_CARD = 2;

type ScopeProps = {
  libraryId: number | undefined;
  periodId: string;
  libraries: ProcessingLibrary[];
  setParam: SetParam;
};

/** The workflow and how far back, two pickers side by side. */
function ActivityScope({
  libraryId,
  periodId,
  libraries,
  setParam,
}: ScopeProps) {
  const workflowLabel = useId();
  const periodLabel = useId();
  return (
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
  );
}

/**
 * What narrows the list: the search, the kinds of file with their counts, and the workflow and how far back. They
 * sit on the header's title line, after the title. Short of room, the search becomes a mark, then the two pickers move
 * into the Files card's header (`cardSlot`), then the last kinds of file fold into a "More" menu: nothing scrolls and
 * no chip is cut. Where the title line is not shared (a phone), only the chips fold.
 */
export function ActivityFilters({
  query,
  group,
  counts,
  libraryId,
  periodId,
  libraries,
  setParam,
  cardSlot,
}: ScopeProps & {
  query: string;
  group: ActivityGroup;
  counts: Record<ActivityGroup, number>;
  /** The element in the Files card's header the pickers move into; null where the page has no card to hold them. */
  cardSlot: HTMLElement | null;
}) {
  const [search, setSearch] = useState(query);
  const [chipsRow, setChipsRow] = useState<HTMLDivElement | null>(null);
  const sharesTitleLine = useMediaQuery("(min-width: 921px)");
  const steps = cardSlot ? PICKERS_IN_CARD : SEARCH_AS_MARK;
  const { stage, folded } = useTitleLineFit({
    row: chipsRow,
    stages: sharesTitleLine ? steps : 0,
    chips: ACTIVITY_GROUPS.length,
    refit: `${group}|${search !== ""}|${cardSlot !== null}|${JSON.stringify(counts)}`,
  });
  const hidden = new Set(
    [
      ...foldedChipIndexes(
        ACTIVITY_GROUPS.length,
        folded,
        ACTIVITY_GROUPS.findIndex((g) => g.id === group),
      ),
    ].map((index) => ACTIVITY_GROUPS[index].id),
  );
  const scope = (
    <ActivityScope
      libraryId={libraryId}
      periodId={periodId}
      libraries={libraries}
      setParam={setParam}
    />
  );
  return (
    <ShellHeaderSlot>
      <div className="mm-history-controls" data-testid="activity-filters">
        <form
          className="mm-history-search"
          role="search"
          onSubmit={(event) => {
            event.preventDefault();
            setParam("q", search.trim());
          }}
        >
          <HeaderSearch
            label="Find a file"
            placeholder="Find a file"
            className="mm-history-search-box"
            collapsed={stage >= SEARCH_AS_MARK}
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            onBlur={() => setParam("q", search.trim())}
          />
        </form>
        <div className="mm-history-chips" ref={setChipsRow}>
          <SegmentedControl
            ariaLabel="Show"
            value={group}
            options={ACTIVITY_GROUPS.map((g) => ({
              value: g.id,
              label: (
                <>
                  {g.label}{" "}
                  <span className="mm-segmented__count">{counts[g.id]}</span>
                </>
              ),
            }))}
            onChange={(next) => setParam("show", next === "all" ? null : next)}
            fold={{ hidden, menuLabel: "More kinds of file" }}
          />
        </div>
        {cardSlot && stage >= PICKERS_IN_CARD ? null : scope}
      </div>
      {cardSlot && stage >= PICKERS_IN_CARD
        ? createPortal(scope, cardSlot)
        : null}
    </ShellHeaderSlot>
  );
}
