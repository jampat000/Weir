/**
 * One card of the Pipeline. Every card is one template, the same size wherever it is:
 *
 *   tile (2:3, as tall as the card allows) | title (one or two lines)
 *                                          | one status line (the lead bold in the station's colour)
 *                                          | a thin bar, moving while work is under way
 *                                          | detail lines: as many whole ones as the card's height holds
 *
 * The station's colour is the card's left edge. A delivered card is full and green for a moment before its
 * tile flies to Just finished; a card whose file stopped is edged in the colour of what happened. Clicking a
 * card opens the file's story, as every card on the page does; a library clean has no file to open.
 */
import type { CSSProperties, ReactElement } from "react";

import { Poster } from "../../../components/shared/poster";
import { classNames } from "../../../lib/ui/class-names";
import { motionAllowed } from "../../../lib/ui/motion-allowed";
import type { ProcessingFile } from "../../../lib/processing/files-api";
import { baseName } from "../../../lib/format/path";
import { CARD_ATTRIBUTE, DETAILS_ATTRIBUTE } from "./fit-details";
import type {
  CardStatus,
  DetailLine,
  PipelineCard as Card,
} from "./pipeline-card-types";
import { TILE_ATTRIBUTE } from "./use-delivery-flight";

/** Where a card sits on the lanes: its station's column and its row. */
export type CardPlace = {
  column: number;
  stations: number;
  /** The row's top, in px. */
  top: number;
  /** Beyond the rows that fit: kept in place (so it can glide back) but not shown. */
  hidden: boolean;
};

function StatusLine({ status }: { status: CardStatus }) {
  const lead =
    status.lead && status.text.startsWith(status.lead) ? status.lead : null;
  return (
    <span className={`mm-pipe__status mm-pipe__status--${status.tone}`}>
      {status.pulse ? (
        <span aria-hidden="true" className="mm-pipe__pulse" />
      ) : null}
      <span className="mm-pipe__status-text">
        {lead ? <b>{lead}</b> : null}
        {lead ? status.text.slice(lead.length) : status.text}
      </span>
    </span>
  );
}

function Detail({ line }: { line: DetailLine }) {
  return (
    <span className="mm-pipe__detail">
      <span className={line.mono ? "mm-pipe__file" : undefined}>
        {line.parts.map((part, index) =>
          typeof part === "string" ? (
            <span key={index}>{part}</span>
          ) : (
            <b key={index}>{part.bold}</b>
          ),
        )}
      </span>
      <span>{line.right}</span>
    </span>
  );
}

function tooltip(card: Card): string {
  const source = card.source === "library" ? "Library clean" : "Download";
  return [card.title, source, card.workflow, baseName(card.path)]
    .filter(Boolean)
    .join(" · ");
}

export function PipelineCardView({
  card,
  place,
  arrived,
  onOpen,
}: {
  card: Card;
  place?: CardPlace;
  /** The card has just changed station, and glows as it arrives. */
  arrived: boolean;
  onOpen: (file: ProcessingFile) => void;
}): ReactElement {
  const style: CSSProperties = {};
  if (place) {
    style.left = `calc(${place.column} * 100% / ${place.stations} + 6px)`;
    style.top = `${place.top}px`;
  }
  const className = classNames(
    "mm-pipe__card",
    card.end === "delivered" && "mm-pipe__card--delivered",
    (card.end === "failed" || card.end === "rejected") &&
      `mm-pipe__card--${card.end}`,
    place?.hidden && "mm-pipe__card--hidden",
    arrived && motionAllowed() && "mm-pipe__card--arrived",
  );
  const common = {
    className,
    style,
    title: tooltip(card),
    "aria-label": `${card.title}: ${card.status.text}`,
    "aria-hidden": place?.hidden ? true : undefined,
    "data-stage": card.stage,
    [CARD_ATTRIBUTE]: "",
  };
  const body = (
    <>
      <span {...{ [TILE_ATTRIBUTE]: card.key }} className="mm-pipe__tile">
        <Poster
          url={card.file?.poster_url}
          title={card.title}
          workflow={card.workflow}
        />
      </span>
      <span className="mm-pipe__text">
        <span className="mm-pipe__title">{card.title}</span>
        <StatusLine status={card.status} />
        {card.bar ? (
          <span
            aria-hidden="true"
            className={classNames(
              "mm-pipe__bar",
              card.bar.moving && "mm-pipe__bar--moving",
              card.bar.waiting && "mm-pipe__bar--waiting",
            )}
          >
            <i style={{ width: `${card.bar.width}%` }} />
          </span>
        ) : null}
        {card.details.length > 0 ? (
          <span className="mm-pipe__details" {...{ [DETAILS_ATTRIBUTE]: "" }}>
            {card.details.map((line, index) => (
              <Detail key={index} line={line} />
            ))}
          </span>
        ) : null}
      </span>
    </>
  );
  const { file } = card;
  return file ? (
    <button
      type="button"
      {...common}
      tabIndex={place?.hidden ? -1 : undefined}
      onClick={() => onOpen(file)}
    >
      {body}
    </button>
  ) : (
    <div role="group" {...common}>
      {body}
    </div>
  );
}
