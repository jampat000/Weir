import { FileName } from "../../components/shared/file-name";
import type { ProcessingFile } from "../../lib/processing/files-api";
import { SourceTag } from "./lane-cards";
import type { WorkingItem } from "./processing-model";
import {
  removedTrackWords,
  timeLeft,
  workingFigures,
} from "./processing-words";
import { StageFlow } from "./stage-flow";

/** What the pass is doing, as label and value pairs, in numbers from the server's own progress and the file's size and length. */
function workingStats(item: WorkingItem): [string, string][] {
  const figures = workingFigures(item);
  return [
    figures.speed ? ["Speed", `${figures.speed} real time`] : null,
    figures.reading ? ["Reading", figures.reading] : null,
    figures.through ? ["Through the file", figures.through] : null,
    figures.running ? ["Running for", figures.running] : null,
  ].filter((stat): stat is [string, string] => stat !== null);
}

/** Cleaning in place reports no percentage, so the bar only says the pass is running. */
function CleaningBar({ name }: { name: string }) {
  return (
    <div
      className="mm-live-bar"
      role="progressbar"
      aria-label={`Progress for ${name}`}
    >
      <span className="mm-live-bar__fill" />
    </div>
  );
}

export function WorkingCard({
  item,
  onOpen,
}: {
  item: WorkingItem;
  onOpen: (file: ProcessingFile) => void;
}) {
  const writing = item.percent != null;
  const removed = removedTrackWords(item);
  const stats = writing ? workingStats(item) : [];
  const file = item.file;
  return (
    <li className="mm-live-card mm-live-card--work" data-testid="live-working">
      <div className="mm-live-work__top">
        <div className="mm-live-card__names">
          <SourceTag source={item.source} libraryName={item.libraryName} />
          <span className="mm-live-card__title mm-live-card__title--lg">
            {file ? (
              <button
                type="button"
                className="mm-live-card__open"
                onClick={() => onOpen(file)}
              >
                {item.name}
              </button>
            ) : (
              item.name
            )}
          </span>
          <FileName path={item.path} className="mm-live-card__file" />
          <span className="mm-live-card__sub">{item.facts}</span>
        </div>
        <div className="mm-live-work__pct">
          <span className="mm-live-work__number">
            {writing ? `${Math.floor(item.percent ?? 0)}%` : ""}
          </span>
          <span className="mm-live-card__sub">
            {writing
              ? timeLeft(item.etaSeconds)
              : item.source === "library"
                ? "Cleaning in place"
                : "Checking the file"}
          </span>
        </div>
      </div>
      {item.source === "download" ? (
        <StageFlow position={item.step} percent={item.percent} />
      ) : (
        <CleaningBar name={item.name} />
      )}
      {stats.length ? (
        <dl className="mm-live-work__stats" data-testid="live-working-stats">
          {stats.map(([label, value]) => (
            <div key={label} className="mm-live-work__stat">
              <dt>{label}</dt>
              <dd>{value}</dd>
            </div>
          ))}
        </dl>
      ) : null}
      {removed.length ? (
        <p className="mm-live-work__facts">
          <span>
            Removing <b>{removed.join(", ")}</b>
          </span>
        </p>
      ) : null}
    </li>
  );
}
