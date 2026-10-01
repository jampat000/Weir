/**
 * The tiles of the Just finished shelf. A tile is the title's poster (or its initials on its workflow's
 * colour where there is none), with how much the file shrank by at its foot and, under that, the workflow's
 * name, which goes to that workflow in the library. The tile of a file that has just been delivered arrives
 * with a ring and flies in from the Pipeline (see delivery-flight).
 */
import type { CSSProperties, ReactElement, RefObject } from "react";
import { Link } from "react-router-dom";

import { Poster } from "../../../components/shared/poster";
import type { FinishedFile } from "../../../lib/activity/processing-outcome";
import { classNames } from "../../../lib/ui/class-names";
import { SHELF_TILE_ATTRIBUTE } from "./delivery-flight";
import type { ShelfTile } from "./shelf-model";
import { useOpenSlots } from "./shelf-slots";
import { TILE_KEY_ATTRIBUTE, useSlideNeighbours } from "./use-slide-neighbours";
import { useTileArrivals } from "./use-tile-arrivals";

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
  const workflow = tile.workflowKnown ? ` (${tile.workflow})` : "";
  const library = tile.item.libraryId;
  return (
    <li
      {...{ [TILE_KEY_ATTRIBUTE]: tile.key }}
      className={classNames(
        "mm-shelf__item",
        tile.fresh && "mm-shelf__item--fresh",
      )}
    >
      <button
        type="button"
        className="mm-shelf__open"
        title={[
          tile.title,
          tile.workflowKnown ? tile.workflow : null,
          tile.detail,
          tile.ago,
        ]
          .filter(Boolean)
          .join(" · ")}
        aria-label={`${tile.title}${workflow}: ${tile.what}${when}`}
        onClick={() => onOpen(tile.item)}
      >
        <span
          className="mm-shelf__art"
          {...{ [SHELF_TILE_ATTRIBUTE]: tile.path }}
        >
          <Poster
            url={tile.item.posterUrl}
            title={tile.title}
            workflow={tile.workflow}
          />
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
      {tile.workflowKnown && library != null ? (
        <Link
          to={`/library?library=${library}`}
          className="lsh-tag"
          title={`Open ${tile.workflow} in the library`}
          onClick={(event) => event.stopPropagation()}
        >
          {tile.workflow}
        </Link>
      ) : null}
    </li>
  );
}

export function ShelfTiles({
  tiles,
  tilesShown,
  captions,
  tileWidth,
  boxRef,
  onOpen,
}: {
  tiles: readonly ShelfTile[];
  /** How many whole tiles the shelf has room for; all of them until it is measured. */
  tilesShown: number | null;
  captions: boolean;
  /** The tile width the shelf's height allows; unset until it is measured. */
  tileWidth: number | null;
  boxRef: RefObject<HTMLUListElement | null>;
  onOpen: (item: FinishedFile) => void;
}): ReactElement {
  // A file whose poster is on its way has a slot that is closed or opening: the tile it pushes out is kept until it is gone.
  const slots = useOpenSlots(boxRef);
  const shown =
    tilesShown === null ? tiles : tiles.slice(0, tilesShown + slots);
  useSlideNeighbours(
    boxRef,
    shown.map((tile) => tile.key).join("|"),
    tileWidth,
  );
  useTileArrivals(boxRef, tiles.map((tile) => tile.key).join("|"));
  const style: CSSProperties | undefined =
    tileWidth === null
      ? undefined
      : { ["--shelf-tile-w" as string]: `${tileWidth}px` };
  return (
    <ul ref={boxRef} className="mm-shelf__row" style={style}>
      {shown.map((tile) => (
        <Tile key={tile.key} tile={tile} captions={captions} onOpen={onOpen} />
      ))}
    </ul>
  );
}
