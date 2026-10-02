/**
 * Just finished: the files Weir has recently handed back, as one shelf of 2:3 tiles, newest first. Chips
 * above it narrow it to one workflow, and the panel's header counts what that choice finished today. Each
 * tile is tagged with its workflow, and the tag goes to that workflow's library. Clicking a tile opens the
 * file's story.
 *
 * Tiles are sized from the height of the shelf, exactly 2:3, and only whole tiles are drawn.
 */
import {
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type ReactElement,
} from "react";

import { Panel } from "../../../components/panels/panel";
import type { FinishedFile } from "../../../lib/activity/processing-outcome";
import { useElementSize } from "../../../lib/ui/use-element-size";
import type { Filter } from "../processing-filter";
import { useFinishedAnnouncement } from "../use-finished-files";
import {
  shelfFit,
  shelfRowNeed,
  tilesAcross,
  type ShelfFit,
} from "./shelf-layout";
import { ShelfFilter } from "./shelf-filter";
import { shelfOf, todayWords } from "./shelf-model";
import { ShelfTiles } from "./shelf-tiles";
import { readShelfWorkflow, saveShelfWorkflow } from "./shelf-workflow-choice";

const NO_NAMES: ReadonlyMap<number, string> = new Map();

type Fit = ShelfFit & { across: number };

export type JustFinishedShelfProps = {
  /** The newest finished files, before any filter. */
  items: readonly FinishedFile[];
  filter: Filter;
  /** Narrows the shelf to this workflow and leaves its chip the only one; the owner's own choice of chip otherwise. */
  workflowId?: number | null;
  now: number;
  /** The names of the workflows, by id: each file's tile carries its workflow's name and colour. */
  workflowNames?: ReadonlyMap<number, string>;
  /** The workflows that have a chip; every named workflow when left out. */
  enabledWorkflowIds?: ReadonlySet<number>;
  /** What the day adds up to across every workflow, when the page knows better than the loaded files tell. */
  count?: string;
  /** Opens a finished file's story. */
  onOpen: (item: FinishedFile) => void;
  /**
   * Told the height of this whole panel that its tiles can use, whenever it changes: any more would leave empty
   * space under the tiles, which the page can give to something else.
   */
  onHeightNeed?: (px: number) => void;
};

export function JustFinishedShelf({
  items,
  filter,
  workflowId = null,
  now,
  workflowNames = NO_NAMES,
  enabledWorkflowIds,
  count,
  onOpen,
  onHeightNeed,
}: JustFinishedShelfProps): ReactElement {
  const [chosen, setChosen] = useState<number | null>(readShelfWorkflow);
  const chips = useMemo(
    () =>
      [...workflowNames]
        .filter(([id]) => enabledWorkflowIds?.has(id) ?? true)
        .map(([id, name]) => ({ id, name })),
    [workflowNames, enabledWorkflowIds],
  );
  const picked = chips.some((chip) => chip.id === chosen) ? chosen : null;
  const narrowedTo = workflowId ?? picked;
  const choices =
    workflowId === null
      ? [{ id: null, name: "All" }, ...chips]
      : chips.filter((chip) => chip.id === workflowId);
  const shelf = useMemo(
    () =>
      shelfOf(items, { filter, workflowId: narrowedTo, now }, workflowNames),
    [items, filter, narrowedTo, now, workflowNames],
  );
  const announcement = useFinishedAnnouncement(items);
  const [shelfRef, shelfSize] = useElementSize<HTMLDivElement>();
  const box = useRef<HTMLUListElement>(null);
  const [fit, setFit] = useState<Fit | null>(null);
  // The tile size comes from the tiles' own box, before paint, so nothing shifts.
  useLayoutEffect(() => {
    const tiles = box.current;
    if (!tiles || !(tiles.clientHeight > 0)) return;
    const size = shelfFit(tiles.clientHeight, tiles.clientWidth);
    const next: Fit = {
      ...size,
      across: tilesAcross(tiles.clientWidth, size.width),
    };
    setFit((current) =>
      current &&
      current.width === next.width &&
      current.caption === next.caption &&
      current.across === next.across
        ? current
        : next,
    );
    // The panel's own chrome (header, chips, padding) is what its height has beyond the row's, and does not change
    // with the height: the need is that and the height the row's tiles can use at this width.
    const panel = tiles.closest<HTMLElement>(".mm-panel");
    if (panel && onHeightNeed) {
      onHeightNeed(
        panel.offsetHeight -
          tiles.clientHeight +
          shelfRowNeed(tiles.clientWidth),
      );
    }
  }, [shelfSize.width, shelfSize.height, onHeightNeed]);
  const countWords =
    picked === null && count !== undefined && !shelf.latest
      ? count
      : todayWords(shelf);
  const choose = (workflowId: number | null) => {
    setChosen(workflowId);
    saveShelfWorkflow(workflowId);
  };
  return (
    <Panel
      title="Just finished"
      count={countWords}
      to="/history"
      toLabel="History"
    >
      <div className="mm-shelf" data-testid="just-finished-shelf">
        <ShelfFilter choices={choices} chosen={narrowedTo} onChoose={choose} />
        <div ref={shelfRef} className="mm-shelf__rows">
          <ShelfTiles
            tiles={shelf.tiles}
            tilesShown={fit?.across ?? null}
            caption={fit?.caption ?? "compact"}
            tileWidth={fit?.width ?? null}
            boxRef={box}
            onOpen={onOpen}
          />
        </div>
        <p
          className="sr-only"
          role="status"
          aria-live="polite"
          data-testid="shelf-announcement"
        >
          {announcement}
        </p>
      </div>
    </Panel>
  );
}
