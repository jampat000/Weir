/**
 * Just finished: the files Weir has recently handed back, as a shelf of 2:3 tiles, newest first. Each tile
 * stands where a poster would (Weir has none, so it is the title's initials on its workflow's colour), with
 * how much the file shrank by across its foot, and under it the title and what was done and when. The tile of
 * a file that has just been delivered arrives with a ring and flies in from the Pipeline (see delivery-flight).
 * Clicking a tile opens the file's story.
 *
 * Tiles are sized from the height of their row, exactly 2:3, and only whole tiles are drawn.
 */
import { useId, useMemo, type CSSProperties, type ReactElement } from "react";
import { Link } from "react-router-dom";

import type { FinishedFile } from "../../../lib/activity/processing-outcome";
import { classNames } from "../../../lib/ui/class-names";
import { useElementSize } from "../../../lib/ui/use-element-size";
import type { Filter } from "../processing-toolbar";
import { useFinishedAnnouncement } from "../use-finished-files";
import { SHELF_TILE_ATTRIBUTE } from "./delivery-flight";
import { shelfFit, tilesAcross } from "./shelf-layout";
import { shelfTiles, type ShelfTile } from "./shelf-model";
import { TitleTile } from "./title-tile";

const NO_WORKFLOWS: ReadonlyMap<number, string> = new Map();

function Tile({
  tile,
  captions,
  onOpen,
}: {
  tile: ShelfTile;
  captions: boolean;
  onOpen: (item: FinishedFile) => void;
}) {
  const when = tile.ago ? `, ${tile.ago}` : "";
  return (
    <li
      className={classNames(
        "mm-shelf__item",
        tile.fresh && "mm-shelf__item--fresh",
      )}
    >
      <button
        type="button"
        className="mm-shelf__open"
        title={[tile.title, tile.detail, tile.ago].filter(Boolean).join(" · ")}
        aria-label={`${tile.title}: ${tile.what}${when}`}
        onClick={() => onOpen(tile.item)}
      >
        <span
          className="mm-shelf__art"
          {...{ [SHELF_TILE_ATTRIBUTE]: tile.path }}
        >
          <TitleTile title={tile.title} workflow={tile.workflow} />
          {tile.saved ? (
            <span className="mm-shelf__saved">{tile.saved}</span>
          ) : null}
        </span>
        {captions ? (
          <span className="mm-shelf__caption">
            {tile.title}
            <small>
              {tile.what}
              {tile.ago ? ` · ${tile.ago}` : ""}
            </small>
          </span>
        ) : null}
      </button>
    </li>
  );
}

export type JustFinishedShelfProps = {
  /** The newest finished files, before any filter. */
  items: readonly FinishedFile[];
  filter: Filter;
  now: number;
  /** The names of the workflows, by id: each file's tile takes its workflow's colour. */
  workflowNames?: ReadonlyMap<number, string>;
  /** Opens a finished file's story. */
  onOpen: (item: FinishedFile) => void;
};

export function JustFinishedShelf({
  items,
  filter,
  now,
  workflowNames = NO_WORKFLOWS,
  onOpen,
}: JustFinishedShelfProps): ReactElement {
  const headingId = useId();
  const tiles = useMemo(
    () => shelfTiles(items, filter, now, workflowNames),
    [items, filter, now, workflowNames],
  );
  const announcement = useFinishedAnnouncement(items);
  const [rowRef, row] = useElementSize<HTMLUListElement>();
  const fit = row.height > 0 ? shelfFit(row.height) : null;
  const shown = fit ? tiles.slice(0, tilesAcross(row.width, fit.width)) : tiles;
  const rowStyle: CSSProperties | undefined = fit
    ? { ["--shelf-tile-w" as string]: `${fit.width}px` }
    : undefined;
  return (
    <section
      className="mm-shelf"
      aria-labelledby={headingId}
      data-testid="just-finished-shelf"
    >
      <header className="mm-shelf__head">
        <h2 id={headingId} className="mm-shelf__heading">
          Just finished
        </h2>
        <span className="mm-shelf__hint">
          Open one to see exactly what Weir did
        </span>
        <Link className="mm-shelf__link" to="/history">
          History →
        </Link>
      </header>
      <ul ref={rowRef} className="mm-shelf__row" style={rowStyle}>
        {shown.length === 0 ? (
          <li className="mm-shelf__empty">Nothing has finished recently.</li>
        ) : (
          shown.map((tile) => (
            <Tile
              key={tile.key}
              tile={tile}
              captions={fit?.captions ?? true}
              onOpen={onOpen}
            />
          ))
        )}
      </ul>
      <p
        className="sr-only"
        role="status"
        aria-live="polite"
        data-testid="shelf-announcement"
      >
        {announcement}
      </p>
    </section>
  );
}
