import { FileName } from "../../components/shared/file-name";
import type { FinishedFile } from "../../lib/activity/processing-outcome";
import { handedBackTone } from "./handed-back-model";
import {
  arrivingDeadline,
  prettyName,
  secondsLeft,
  type ArrivingItem,
  type HandingItem,
  type WaitingItem,
  type WorkSource,
} from "./processing-model";
import {
  ago,
  clock,
  finishedLine,
  ringLabel,
  ringState,
} from "./processing-words";

/** The ring's radius is 15, so its dash pattern is one circumference long. */
const RING_CIRCUMFERENCE = 94.2;
/** How much of the ring a countdown with no known length leaves unlit. */
const RING_UNKNOWN_GAP = 0.72;

/** 0 → "1st", 10 → "11th", 21 → "22nd". */
export function ordinal(index: number): string {
  const n = index + 1;
  const teens = n % 100 >= 11 && n % 100 <= 13;
  return `${n}${teens ? "th" : (["th", "st", "nd", "rd"][n % 10] ?? "th")}`;
}

export function SourceTag({
  source,
  libraryName,
}: {
  source: WorkSource;
  libraryName: string;
}) {
  return (
    <span className={`mm-live-tag mm-live-tag--${source}`}>
      {source === "library" ? "Library" : "Download"} · {libraryName}
    </span>
  );
}

/** How far through its wait a file is, or null when the wait has no known length. */
function ringFraction(item: ArrivingItem, left: number | null): number | null {
  const state = ringState(left);
  if (state === "checking") return 1;
  const total =
    item.holdUntil != null ? item.holdTotal : item.nextLook?.interval;
  if (state !== "counting" || !total || left == null) return null;
  return Math.min(1, Math.max(0, 1 - left / total));
}

function Ring({ item, now }: { item: ArrivingItem; now: number }) {
  const left = secondsLeft(arrivingDeadline(item), now);
  const state = ringState(left);
  const fraction = ringFraction(item, left);
  const label =
    state === "counting" && left != null
      ? ringLabel(left)
      : state === "checking"
        ? "now"
        : "–";
  return (
    <svg
      className={`mm-live-ring mm-live-ring--${state}`}
      width="38"
      height="38"
      viewBox="0 0 38 38"
      aria-hidden="true"
    >
      <circle className="mm-live-ring__track" cx="19" cy="19" r="15" />
      <circle
        className={`mm-live-ring__fill${item.upstream ? " mm-live-ring__fill--upstream" : ""}`}
        cx="19"
        cy="19"
        r="15"
        strokeDasharray={RING_CIRCUMFERENCE}
        strokeDashoffset={
          RING_CIRCUMFERENCE *
          (fraction == null ? RING_UNKNOWN_GAP : 1 - fraction)
        }
      />
      <text x="19" y="23" textAnchor="middle" className="mm-live-ring__text">
        {label}
      </text>
    </svg>
  );
}

/** What an arriving file is waiting for, and when Weir looks at it next. */
function arrivingNote(item: ArrivingItem, left: number | null): string {
  const waitsOnWeir = item.holdUntil == null && item.nextLook != null;
  if (ringState(left) === "checking") {
    if (item.upstream) return `${item.note} Weir is checking again now.`;
    if (waitsOnWeir) return `${item.note} Weir is looking again now.`;
    return "Its wait is over. Weir is checking it now.";
  }
  return waitsOnWeir && left != null
    ? `${item.note} Weir looks again in ${clock(left)}.`
    : item.note;
}

export function ArrivingCard({
  item,
  now,
}: {
  item: ArrivingItem;
  now: number;
}) {
  const left = secondsLeft(arrivingDeadline(item), now);
  return (
    <li className="mm-live-card" data-testid="live-arriving">
      <div className="mm-live-card__row">
        <Ring item={item} now={now} />
        <div className="mm-live-card__names">
          <span className="mm-live-card__title">{item.name}</span>
          <FileName path={item.path} className="mm-live-card__file" />
          <span className="mm-live-card__sub">{item.facts}</span>
        </div>
      </div>
      <p className="mm-live-card__note">{arrivingNote(item, left)}</p>
    </li>
  );
}

export function WaitingCard({
  item,
  index,
}: {
  item: WaitingItem;
  index: number;
}) {
  return (
    <li className="mm-live-card" data-testid="live-waiting">
      <div className="mm-live-card__meta">
        <span className="mm-live-card__sub">{ordinal(index)} in line</span>
        <SourceTag source={item.source} libraryName={item.libraryName} />
      </div>
      <span className="mm-live-card__title">{item.name}</span>
      <FileName path={item.path} className="mm-live-card__file" />
      <span className="mm-live-card__sub">{item.note ?? item.facts}</span>
    </li>
  );
}

export function HandingCard({ item }: { item: HandingItem }) {
  return (
    <li className="mm-live-card" data-testid="live-handing">
      <SourceTag source={item.source} libraryName={item.libraryName} />
      <span className="mm-live-card__title">{item.name}</span>
      <FileName path={item.path} className="mm-live-card__file" />
      <p className="mm-live-card__note mm-live-card__note--busy">
        <span className="mm-live-spin" aria-hidden="true" />
        {item.source === "library"
          ? "Swapping it into place and telling your media manager"
          : "Checking the new file, then handing it back"}
      </p>
    </li>
  );
}

export function FinishedRow({
  item,
  now,
  onOpen,
}: {
  item: FinishedFile;
  now: number;
  onOpen: (item: FinishedFile) => void;
}) {
  const tone = handedBackTone(item);
  const name = prettyName(item.relativePath);
  const line = finishedLine(item);
  return (
    <li className="mm-live-done" data-testid="live-finished">
      <i className={`mm-live-dot mm-live-dot--${tone}`} aria-hidden="true" />
      <div className="mm-live-card__names">
        <span className="mm-live-done__top">
          <button
            type="button"
            className="mm-live-card__open mm-live-card__title"
            title={name}
            onClick={() => onOpen(item)}
          >
            {name}
          </button>
          <span className="mm-live-done__ago">{ago(item.finishedAt, now)}</span>
        </span>
        <FileName path={item.relativePath} className="mm-live-card__file" />
        <span className="mm-live-card__sub mm-live-card__sub--wrap">
          {line}
        </span>
      </div>
    </li>
  );
}
