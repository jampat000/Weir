/**
 * The tiles of the Just finished shelf. A tile stands where a poster would (Weir has none, so it is the
 * title's initials on its workflow's colour) with the workflow's name across its top, which goes to that
 * workflow in the library, and how much the file shrank by across its foot. The tile of a file that has
 * just been delivered arrives with a ring and flies in from the Pipeline (see delivery-flight).
 */
import type { CSSProperties, ReactElement, Ref } from "react";
import { Link } from "react-router-dom";

import type { FinishedFile } from "../../../lib/activity/processing-outcome";
import { classNames } from "../../../lib/ui/class-names";
import { SHELF_TILE_ATTRIBUTE } from "./delivery-flight";
import type { ShelfTile } from "./shelf-model";
import { TitleTile } from "./title-tile";

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
      {tile.workflowKnown && library != null ? (
        <Link
          to={`/library?library=${library}`}
          className="mm-shelf__tag"
          title={`Open ${tile.workflow} in the library`}
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
  boxRef: Ref<HTMLUListElement>;
  onOpen: (item: FinishedFile) => void;
}): ReactElement {
  const shown = tilesShown === null ? tiles : tiles.slice(0, tilesShown);
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
