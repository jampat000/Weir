/**
 * The tiles of the Just finished shelf. A tile is the title's poster (or its initials on its workflow's
 * colour where there is none) with the workflow's name in a tag across its foot, and under it a caption of
 * fixed slots, centred, so the lines of every tile in the row stand level: the title (two lines), how the file
 * came out in red, amber, green or grey, and, where the posters are big enough, what was removed and when. Where
 * the row has room for no more than one line, the caption is that status line alone, in the fewest words that fit
 * under the poster or the dot by itself; the rest is in the tile's tooltip and name. On a poster too small for the
 * tag (see posterSize) it gives way to a slim strip in the workflow's colour, and its name goes into the tile's
 * tooltip and name. The tile of a file that has just been delivered arrives with a ring and
 * flies in from the Pipeline (see delivery-flight).
 */
import type { CSSProperties, ReactElement, RefObject } from "react";
import { Link } from "react-router-dom";

import { Poster } from "../../../components/shared/poster";
import type { FinishedFile } from "../../../lib/activity/processing-outcome";
import { useWorkflowHues } from "../../../lib/processing/workflow-hues";
import { classNames } from "../../../lib/ui/class-names";
import { SHELF_TILE_ATTRIBUTE } from "./delivery-flight";
import { STATUS_MARK_PX, lineWords, type MeasureText } from "./caption-fit";
import { posterSize, type CaptionTier } from "./shelf-layout";
import type { ShelfTile, TileStatus } from "./shelf-model";
import { useOpenSlots } from "./shelf-slots";
import { TILE_KEY_ATTRIBUTE, useSlideNeighbours } from "./use-slide-neighbours";
import { useTileArrivals } from "./use-tile-arrivals";

/** Where text cannot be measured, as in a test, everything is taken to fit. */
const FITS_ANYTHING: MeasureText = () => 0;

/** What the status line says under a poster `width` wide: its short words in the tiny caption, its fuller ones otherwise. */
function statusWords(
  status: TileStatus,
  caption: CaptionTier,
  width: number | null,
  measure: MeasureText,
): string {
  const words = caption === "tiny" ? status.short : status.words;
  return width === null
    ? words[0]
    : lineWords(words, width - STATUS_MARK_PX, measure);
}

function Tile({
  tile,
  caption,
  status,
  detail,
  small,
  onOpen,
}: {
  tile: ShelfTile;
  /** What goes under the tile. */
  caption: CaptionTier;
  /** What the status line says at this width. */
  status: string;
  /** What the detail line says at this width, or nothing. */
  detail: string | null;
  /** The poster is too small for the workflow's whole tag. */
  small: boolean;
  onOpen: (item: FinishedFile) => void;
}) {
  const hues = useWorkflowHues();
  const when = tile.ago ? `, ${tile.ago}` : "";
  const workflow = tile.workflowKnown ? ` (${tile.workflow})` : "";
  const library = tile.item.libraryId;
  const saved = tile.savedAmount ? `, saved ${tile.savedAmount}` : "";
  return (
    <li
      {...{ [TILE_KEY_ATTRIBUTE]: tile.key }}
      data-size={small ? "small" : "full"}
      className="mm-shelf__item"
    >
      <button
        type="button"
        className="mm-shelf__open"
        title={[
          tile.title,
          tile.workflowKnown ? tile.workflow : null,
          tile.sentence,
          tile.ago,
        ]
          .filter(Boolean)
          .join(" · ")}
        aria-label={`${tile.title}${workflow}: ${tile.what}${saved}${when}`}
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
        </span>
        {caption === "none" ? null : (
          <span className="mm-shelf__caption" data-tier={caption}>
            {caption === "tiny" ? null : (
              <span className="mm-shelf__line mm-shelf__title">
                {tile.title}
              </span>
            )}
            <span
              className={classNames(
                "mm-shelf__line",
                "mm-shelf__status",
                `mm-shelf__status--${tile.status.tone}`,
              )}
            >
              <span>{status}</span>
            </span>
            {caption === "full" ? (
              <>
                <span className="mm-shelf__line mm-shelf__muted">{detail}</span>
                <span className="mm-shelf__line mm-shelf__muted">
                  {tile.ago}
                </span>
              </>
            ) : null}
          </span>
        )}
      </button>
      {tile.workflowKnown && library != null ? (
        <Link
          to={`/library?library=${library}`}
          className="lsh-tag"
          style={{ ["--tile-hue" as string]: hues.forId(library) }}
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
  caption,
  tileWidth,
  measure,
  boxRef,
  onOpen,
}: {
  tiles: readonly ShelfTile[];
  /** How many whole tiles the shelf has room for; all of them until it is measured. */
  tilesShown: number | null;
  caption: CaptionTier;
  /** The tile width the shelf's height allows; unset until it is measured. */
  tileWidth: number | null;
  /** Measures a line in the caption's font, to see what fits under a poster; null where it cannot be measured. */
  measure: MeasureText | null;
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
  const small = posterSize(tileWidth) === "small";
  const style: CSSProperties | undefined =
    tileWidth === null
      ? undefined
      : { ["--shelf-tile-w" as string]: `${tileWidth}px` };
  return (
    <ul ref={boxRef} className="mm-shelf__row" style={style}>
      {shown.map((tile) => (
        <Tile
          key={tile.key}
          tile={tile}
          caption={caption}
          status={statusWords(
            tile.status,
            caption,
            tileWidth,
            measure ?? FITS_ANYTHING,
          )}
          detail={
            tile.detail === null
              ? null
              : tileWidth === null
                ? tile.detail[0]
                : lineWords(tile.detail, tileWidth, measure ?? FITS_ANYTHING)
          }
          small={small}
          onOpen={onOpen}
        />
      ))}
    </ul>
  );
}
